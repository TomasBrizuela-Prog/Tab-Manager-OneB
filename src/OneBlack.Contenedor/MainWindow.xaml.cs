using Microsoft.Win32;
using OneBlack.Core;
using System;
using System.Diagnostics;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace OneBlack.Contenedor
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        // ... tu código existente ...

        private bool labelsVisibles = true;
        public bool LabelsVisibles
        {
            get => labelsVisibles;
            set { labelsVisibles = value; OnPropertyChanged(nameof(LabelsVisibles)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        private readonly AdoptadorDeVentanas adoptador = new AdoptadorDeVentanas();

        // Recordamos las candidatas que adoptamos, porque tras adoptarlas ya no
        // aparecen en la enumeración (son hijas de OneBlack, no top-level).
        private readonly List<VentanaCandidata> candidatasAdoptadas = new();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Operación de encaje diferida pendiente (la que encola Adoptar).
        // La guardamos para poder CANCELARLA si Devolver corre antes de que se ejecute
        private System.Windows.Threading.DispatcherOperation? encajeDiferido;

        // Campo que recuerda qué ventana adoptada se está mostrando, para poder
        // re-enfocarla cuando OneBlack recupere la activación (ej: tras una notificación
        // del IDE que robó el foco).
        private IntPtr hwndVisibleActual = IntPtr.Zero;

        // Timer de la salvaguarda de repintado. Lo guardamos para poder frenarlo:
        // no queremos que siga disparando tras un Devolver, ni que se apilen timers
        // de adopciones distintas.
        private System.Windows.Threading.DispatcherTimer? repintadoTimer;

        // Timer que mantiene la ventana adoptada clavada en el hueco. Corre suave y
        // barato mientras hay una ventana visible: si VS Code se desencajó por su cuenta
        // (arrastrándolo desde su topbar de Chromium, o por el borde), lo repone. Es la
        // red de seguridad definitiva: no importa CÓMO se movió, si no está encajado, lo
        // reencaja. Reusa ReajustarTamaño, que ya sabe clavarlo en (0,0).
        private System.Windows.Threading.DispatcherTimer? clavadoTimer;

        // La colección de pestañas abiertas. ObservableCollection avisa sola a la UI
        // cuando agregás/quitás elementos — como un array reactivo de Angular. El
        // ItemsControl del XAML la dibuja y se actualiza solo.
        private readonly System.Collections.ObjectModel.ObservableCollection<PestañaVentana> pestañas = new();

        // La pestaña actualmente activa (puede estar vacía u ocupada). null = ninguna
        // (estás en el Cockpit).
        private PestañaVentana? pestañaActiva;
        public MainWindow()
        {
            InitializeComponent();
            // Conectar la colección de pestañas a la barra de pestañas del XAML.
            barraPestañas.ItemsSource = pestañas;
            DataContext = this;   // para que los bindings de la Window funcionen
            MostrarPaginaPropia(paginaCockpit);
            // Re-aplicar el foco de teclado a la ventana adoptada cuando:
            //  (a) OneBlack se activa (Activated), o
            //  (b) el usuario hace click en cualquier parte de OneBlack (PreviewMouseDown).
            // El caso (b) es el que cubre la notificación del IDE que roba el foco: apenas
            // el usuario vuelve a clickear en la app, le devolvemos el teclado al IDE.
            Activated += (s, e) => ReenfocarVisible();
            PreviewMouseDown += (s, e) => ReenfocarVisible();
            //LanzarJanitor();
            //RefrescarCandidatas();
            // Cuando el usuario elige qué abrir en la página nueva pestaña,
            // lo adoptamos en la pestaña activa.
            paginaNuevaPestaña.SeEligioAbrir += async (programa, carpeta) =>
                await AbrirEnPestañaActiva(programa, carpeta);
            // Cuando el hueco cambia de tamaño, reajustar las ventanas adoptadas.
            anfitriona.SizeChanged += (s, e) =>
            {
                var (ancho, alto) = DimensionesFisicas();
                adoptador.ReajustarTamaño(ancho, alto);
            };
        }

        /// <summary>
        /// Re-aplica el foco de teclado a la ventana adoptada que está visible.
        /// Se llama cuando OneBlack recupera actividad (activación o click del usuario),
        /// para recuperar el teclado tras algo que robó el foco (ej: notificación del IDE).
        /// </summary>
        private void ReenfocarVisible()
        {
            if (hwndVisibleActual != IntPtr.Zero && adoptador.YaEstaAdoptada(hwndVisibleActual))
                adoptador.ReaplicarFoco(hwndVisibleActual);   // ← solo foco, no MostrarSolo
        }
        private void LanzarJanitor()
        {
            try
            {
                int miPid = Process.GetCurrentProcess().Id;

                // El janitor vive en la misma carpeta que OneBlack (lo copia el build).
                string carpetaBase = AppDomain.CurrentDomain.BaseDirectory;
                string rutaJanitor = Path.Combine(carpetaBase, "OneBlack.Janitor.exe");

                if (!File.Exists(rutaJanitor))
                {
                    textoEstado.Text = "No encontré el janitor en la carpeta de salida.";
                    return;
                }

                var inicio = new ProcessStartInfo
                {
                    FileName = rutaJanitor,
                    Arguments = miPid.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(inicio);
                textoEstado.Text = "Janitor lanzado. Vigilando.";
            }
            catch (Exception ex)
            {
                textoEstado.Text = $"No pude lanzar el janitor: {ex.Message}";
            }
        }
     

  

        /// <summary>
        /// Salvaguarda de dibujado: tras adoptar, dispara unos pocos repintados espaciados.
        /// Cubre el caso intermitente en que la ventana (VS Code sobre todo, o cualquiera
        /// bajo carga) no dibuja tras un adoptar rápido y queda en negro. Barato: 3 disparos
        /// espaciados y el timer se apaga. Cada disparo es inofensivo si ya se devolvió la
        /// ventana (el core lo ignora), así que no reintroduce la carrera de adoptar/devolver.
        /// </summary>
        private void ProgramarRepintados(IntPtr hwnd)
        {
            // Frenar cualquier salvaguarda anterior antes de arrancar otra.
            repintadoTimer?.Stop();

            int disparos = 0;
            repintadoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            repintadoTimer.Tick += (s, e) =>
            {
                adoptador.ForzarRepintado(hwnd);   // no-op si ya se devolvió
                disparos++;
                if (disparos >= 3)
                    repintadoTimer?.Stop();
            };
            repintadoTimer.Start();
        }
        /// <summary>
        /// Arranca el corrector de posición: cada 500ms reencaja la ventana visible en
        /// el hueco, por si se desencajó por su cuenta. Barato (un MoveWindow cada medio
        /// segundo) e imperceptible. Es la red que atrapa todas las vías por las que
        /// Chromium mueve su ventana, sin pelear contra cada una.
        /// </summary>
        private void ArrancarClavado()
        {
            if (clavadoTimer != null) return;

            int ticks = 0;   // contador para espaciar el refoco

            clavadoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            clavadoTimer.Tick += (s, e) =>
            {
                // Eliminar pestañas de ventanas muertas (esto puede correr seguido, es barato).
                LimpiarPestañasMuertas();

                if (hwndVisibleActual != IntPtr.Zero && adoptador.YaEstaAdoptada(hwndVisibleActual))
                {
                    // El reencaje corre en cada tick (mantener la ventana clavada no molesta).
                    var (a, al) = DimensionesFisicas();
                    adoptador.ReajustarTamaño(a, al);

                    // El REFOCO corre solo 1 de cada 5 ticks (~cada 1s) y solo si OneBlack
                    // está activa. Reafirmarlo tan seguido como el reencaje peleaba con los
                    // clicks del usuario en las pestañas (por eso "no respondía"). Cada segundo
                    // alcanza para recuperar el teclado tras una notificación, sin estorbar.
                    ticks++;
                    if (ticks >= 5)
                    {
                        ticks = 0;
                        if (this.IsActive)
                            adoptador.ReaplicarFoco(hwndVisibleActual);
                    }
                }
            };
            clavadoTimer.Start();
        }

        /// <summary>Frena el corrector de posición (cuando no hay nada adoptado visible).</summary>
        private void FrenarClavado()
        {
            clavadoTimer?.Stop();
            clavadoTimer = null;
        }



        /// <summary>
        /// Activa una pestaña: muestra su ventana (ocultando el resto), marca cuál está
        /// activa para que la UI la resalte, y arranca las salvaguardas (foco, repintado,
        /// clavado) sobre ella. Es el "cambio de pestaña".
        /// </summary>
        /// <summary>
        /// Activa una pestaña. Si está OCUPADA, muestra su IDE (hueco). Si está VACÍA,
        /// muestra la página de nueva pestaña. Marca cuál está activa para la UI.
        /// </summary>
        private void ActivarPestaña(PestañaVentana pestaña)
        {
            if (pestaña == null) return;

            pestañaActiva = pestaña;

            // Marcar estado activo (la UI resalta la activa).
            foreach (var p in pestañas)
                p.EstaActiva = (p == pestaña);

            if (pestaña.EstaVacia)
            {
                // Pestaña vacía → mostrar la página de nueva pestaña.
                // Ocultar cualquier IDE que estuviera visible.
                if (hwndVisibleActual != IntPtr.Zero)
                {
                    adoptador.OcultarVentana(hwndVisibleActual);
                    hwndVisibleActual = IntPtr.Zero;
                }
                MostrarPaginaPropia(paginaNuevaPestaña);
            }
            else
            {
                // Pestaña ocupada → mostrar su IDE.
                MostrarHueco();
                hwndVisibleActual = pestaña.Hwnd;
                adoptador.MostrarSolo(pestaña.Hwnd);
                ProgramarRepintados(pestaña.Hwnd);
                ReaplicarFocoDiferido(pestaña.Hwnd);
                ArrancarClavado();
            }

            textoEstado.Text = pestaña.EstaVacia
                ? "Nueva pestaña."
                : $"{pestaña.Titulo} · {pestaña.Programa?.NombreMostrado}";
        }

        /// <summary>
        /// Lanza un programa y, cuando aparece su ventana, la adopta EN LA PESTAÑA ACTIVA
        /// (la transforma de vacía a ocupada). Es el flujo "elegí algo en la nueva pestaña".
        /// </summary>
        private async Task AbrirEnPestañaActiva(ProgramaSoportado programa, string? carpeta)
        {
            // Guarda anti-duplicado: si ya hay una pestaña OCUPADA con esta carpeta, activá esa.
            if (!string.IsNullOrWhiteSpace(carpeta))
            {
                var existente = pestañas.FirstOrDefault(p =>
                    !p.EstaVacia &&
                    string.Equals(p.Carpeta, carpeta, StringComparison.OrdinalIgnoreCase));
                if (existente != null)
                {
                    ActivarPestaña(existente);
                    textoEstado.Text = $"{existente.Titulo} ya está abierto.";
                    return;
                }
            }

            // La pestaña donde vamos a adoptar: la activa (que debería estar vacía).
            var destino = pestañaActiva;
            if (destino == null || !destino.EstaVacia)
            {
                // Si por algún motivo no hay una pestaña vacía activa, creamos una.
                destino = new PestañaVentana();
                pestañas.Add(destino);
                ActivarPestaña(destino);
            }

            textoEstado.Text = $"Lanzando {programa.NombreMostrado}…";

            IntPtr hwnd = await lanzador.LanzarYEsperar(programa, carpeta);
            if (hwnd == IntPtr.Zero)
            {
                textoEstado.Text = "La ventana no apareció (timeout).";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            anfitriona.UpdateLayout();
            var (ancho, alto) = DimensionesFisicas();

            if (adoptador.Adoptar(hwnd, hwndContenedor, ancho, alto))
            {
                string titulo = string.IsNullOrWhiteSpace(carpeta)
                    ? programa.NombreMostrado
                    : System.IO.Path.GetFileName(carpeta.TrimEnd('\\'));

                // TRANSFORMAR la pestaña vacía en ocupada.
                destino.Ocupar(hwnd, programa, carpeta, titulo);

                // Mostrarla ya como IDE.
                ActivarPestaña(destino);
                textoEstado.Text = $"{titulo} abierto.";
            }
            else textoEstado.Text = "Apareció la ventana pero falló la adopción.";
        }
        /// <summary>
        /// Handler del click en una pestaña. El botón de la pestaña lleva la PestañaVentana
        /// en su DataContext (viene del binding), así la recuperamos y la activamos.
        /// </summary>
        private void Pestaña_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PestañaVentana pestaña)
                ActivarPestaña(pestaña);
        }

        /// <summary>
        /// Re-aplica el foco de teclado un instante después de adoptar una ventana
        /// recién lanzada. Necesario porque al lanzar+adoptar en <1s, el thread de
        /// input del programa todavía no maduró cuando MostrarSolo corre su
        /// AttachThreadInput, y el teclado no engancha. Re-mostrar cuando ya maduró
        /// lo resuelve. Un solo disparo diferido alcanza.
        /// </summary>
        private void ReaplicarFocoDiferido(IntPtr hwnd)
        {
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                // Solo si sigue adoptada (pudo haberse devuelto en el ínterin).
                if (adoptador.YaEstaAdoptada(hwnd))
                    adoptador.MostrarSolo(hwnd);   // re-corre AttachThreadInput + WM_ACTIVATE
            };
            timer.Start();
        }
        //prueba
        private readonly LanzadorDeProgramas lanzador = new LanzadorDeProgramas();

        private async void botonProyectoPrueba_Click(object sender, RoutedEventArgs e)
        {
            var vscode = CatalogoDeProgramas.Buscar("Code");
            if (vscode == null) { textoEstado.Text = "VS Code no está en el catálogo."; return; }

            // Crear una pestaña vacía, activarla, y adoptar en ella (simula el flujo real).
            var nueva = new PestañaVentana();
            pestañas.Add(nueva);
            ActivarPestaña(nueva);

            await AbrirEnPestañaActiva(vscode, @"C:\Dev\Tesis\ProyectosPrueba\RECUPERATORIO");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Cierre ordenado: soltamos todas las ventanas adoptadas ANTES de morir.
            // Así vuelven al escritorio sanas y el archivo de estado se limpia solo.
            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            adoptador.DevolverTodas(hwndContenedor);

            base.OnClosing(e);
        }
        /// <summary>
        /// Suelta todas las ventanas adoptadas. Lo llama el manejador de excepciones
        /// no manejadas como último recurso antes de que la app muera por un error.
        /// </summary>
        public void SoltarTodoDeEmergencia()
        {
            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            adoptador.DevolverTodas(hwndContenedor);
        }

        /// <summary>
        /// Revisa si alguna pestaña apunta a una ventana que ya murió (el usuario cerró
        /// el IDE por la X, Alt+F4, crash, etc.) y la elimina. Sin esto, quedaría una
        /// pestaña huérfana apuntando a una ventana inexistente, y relanzar duplicaría.
        /// Barato: recorre la lista de pestañas y chequea IsWindow vía el adoptador.
        /// </summary>
        private void LimpiarPestañasMuertas()
        {
            // Solo las OCUPADAS pueden tener ventana muerta; las vacías no tienen Hwnd.
            var muertas = pestañas
                .Where(p => !p.EstaVacia && !adoptador.VentanaSigueViva(p.Hwnd))
                .ToList();

            if (muertas.Count == 0)
                return;

            foreach (var muerta in muertas)
            {
                adoptador.Devolver(muerta.Hwnd, anfitriona.ObtenerHwndContenedor());
                pestañas.Remove(muerta);
                if (hwndVisibleActual == muerta.Hwnd)
                    hwndVisibleActual = IntPtr.Zero;
                if (pestañaActiva == muerta)
                    pestañaActiva = null;
            }

            // Si quedó alguna pestaña, activamos la primera; si no, volvemos al Cockpit.
            if (pestañaActiva == null)
            {
                if (pestañas.Count > 0)
                    ActivarPestaña(pestañas[0]);
                else
                    navCockpit_Click(this, new RoutedEventArgs());   // volver al home
            }

            textoEstado.Text = "Se cerró una ventana; pestaña eliminada.";
        }

        /// <summary>
        /// Convierte las dimensiones lógicas de WPF a píxeles físicos reales, según
        /// el escalado de la pantalla (DPI). WPF trabaja en unidades lógicas; Win32
        /// espera píxeles físicos. Con escala 125%, 800 lógicos = 1000 físicos.
        /// </summary>
        private (int ancho, int alto) DimensionesFisicas()
        {
            double anchoLogico = anfitriona.ActualWidth;
            double altoLogico = anfitriona.ActualHeight;

            // Obtener el factor de escala de esta ventana.
            var source = PresentationSource.FromVisual(this);
            double escalaX = 1.0, escalaY = 1.0;
            if (source?.CompositionTarget != null)
            {
                escalaX = source.CompositionTarget.TransformToDevice.M11;  // ej: 1.25
                escalaY = source.CompositionTarget.TransformToDevice.M22;
            }

            int anchoFisico = (int)(anchoLogico * escalaX);
            int altoFisico = (int)(altoLogico * escalaY);
            return (anchoFisico, altoFisico);
        }



        // ===== Handlers del chrome, cableados en seco (se conectan en su módulo) =====

        /// <summary>
        /// "+": abre un selector de carpeta y lanza VS Code apuntando a ella, como una
        /// pestaña nueva. Reemplaza el botón de prueba con carpeta hardcodeada: ahora el
        /// usuario elige qué proyecto abrir.
        /// </summary>
        private void botonAgregar_Click(object sender, RoutedEventArgs e)
        {
            // Crear una pestaña VACÍA y activarla (muestra la página nueva pestaña).
            var nueva = new PestañaVentana();
            pestañas.Add(nueva);
            ActivarPestaña(nueva);
        }
      
        // Instancias únicas de las páginas propias (se crean una vez y se reusan).
        private readonly PaginaCockpit paginaCockpit = new();
        private readonly PaginaNuevaPestaña paginaNuevaPestaña = new();

        /// <summary>
        /// Muestra una página propia de OneBlack (Cockpit, nueva pestaña) en el área
        /// central. OCULTA el hueco y cualquier ventana adoptada visible, porque el IDE
        /// pinta encima de su región y taparía la página. Desmarca la pestaña de IDE activa.
        /// </summary>
        private void MostrarPaginaPropia(System.Windows.Controls.UserControl pagina)
        {
            // Ocultar la ventana adoptada que estuviera visible (SW_HIDE vía el adoptador).
            if (hwndVisibleActual != IntPtr.Zero)
            {
                adoptador.OcultarVentana(hwndVisibleActual);
                hwndVisibleActual = IntPtr.Zero;
            }

            // Ninguna pestaña de IDE queda activa.
            foreach (var p in pestañas)
                p.EstaActiva = false;

            // Ocultar el hueco, mostrar la página propia.
            marcoHueco.Visibility = Visibility.Collapsed;
            areaPaginaPropia.Content = pagina;
            areaPaginaPropia.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Muestra el hueco (para un IDE). Oculta las páginas propias. Es el complemento
        /// de MostrarPaginaPropia: cuando activás una pestaña de IDE, volvemos al hueco.
        /// </summary>
        private void MostrarHueco()
        {
            areaPaginaPropia.Visibility = Visibility.Collapsed;
            areaPaginaPropia.Content = null;
            marcoHueco.Visibility = Visibility.Visible;
        }
        // Estado del plegado de la sidebar.

        private bool sidebarPlegada = false;



        private void botonPlegar_Click(object sender, RoutedEventArgs e)
        {
            sidebarPlegada = !sidebarPlegada;
            colSidebar.Width = new GridLength(sidebarPlegada ? 56 : 212);
            LabelsVisibles = !sidebarPlegada;   // los labels reaccionan solos vía binding
            txtWordmark.Visibility = txtHub.Visibility = txtGrupoEspacio.Visibility =
                sidebarPlegada ? Visibility.Collapsed : Visibility.Visible;
            botonPlegar.Content = sidebarPlegada ? "»" : "«";

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var (ancho, alto) = DimensionesFisicas();
                adoptador.ReajustarTamaño(ancho, alto);
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        // Navegación de espacios — Capa 2: cambiar la vista central.
        private void navCockpit_Click(object sender, RoutedEventArgs e)
        {
            MostrarPaginaPropia(paginaCockpit);
            textoEstado.Text = "Cockpit.";
        }
        private void navProyectos_Click(object sender, RoutedEventArgs e)
        {
            textoEstado.Text = "Vista Proyectos: próximamente.";
        }
        private void navGit_Click(object sender, RoutedEventArgs e)
        {
            textoEstado.Text = "Vista Git: próximamente.";
        }
        private void navAjustes_Click(object sender, RoutedEventArgs e)
        {
            textoEstado.Text = "Vista Ajustes: próximamente.";
        }
    }
}