using OneBlack.Core;
using System;
using System.Diagnostics;
using System.Windows;
using System.Diagnostics;
using System.IO;

namespace OneBlack.Contenedor
{
    public partial class MainWindow : Window
    {
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
        public MainWindow()
        {
            InitializeComponent();
           
            // Re-aplicar el foco de teclado a la ventana adoptada cuando:
            //  (a) OneBlack se activa (Activated), o
            //  (b) el usuario hace click en cualquier parte de OneBlack (PreviewMouseDown).
            // El caso (b) es el que cubre la notificación del IDE que roba el foco: apenas
            // el usuario vuelve a clickear en la app, le devolvemos el teclado al IDE.
            Activated += (s, e) => ReenfocarVisible();
            PreviewMouseDown += (s, e) => ReenfocarVisible();
            //LanzarJanitor();
            //RefrescarCandidatas();

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
            if (clavadoTimer != null) return;   // ya está corriendo

            clavadoTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            clavadoTimer.Tick += (s, e) =>
            {
                if (hwndVisibleActual != IntPtr.Zero && adoptador.YaEstaAdoptada(hwndVisibleActual))
                {
                    var (a, al) = DimensionesFisicas();
                    adoptador.ReajustarTamaño(a, al);       // el reencaje sí corre siempre

                    // El foco SOLO se reafirma si OneBlack está en primer plano. Si el usuario
                    // se fue a otra app, no le robamos el foco de vuelta cada 200ms.
                    if (this.IsActive)
                        adoptador.ReaplicarFoco(hwndVisibleActual);
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

            textoEstado.Text = "Lanzando VS Code…";

            //
            string carpeta = @"C:\Dev\Tesis\ProyectosPrueba\RECUPERATORIO";

            IntPtr hwnd = await lanzador.LanzarYEsperar(vscode, carpeta);
            if (hwnd == IntPtr.Zero)
            {
                textoEstado.Text = "La ventana no apareció (timeout).";
                return;
            }

            // Apareció: adoptarla, igual que el flujo manual.
            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            anfitriona.UpdateLayout();
            var (ancho, alto) = DimensionesFisicas();

            if (adoptador.Adoptar(hwnd, hwndContenedor, ancho, alto))
            {
                hwndVisibleActual = hwnd;
                adoptador.MostrarSolo(hwnd);
                ProgramarRepintados(hwnd);
                ReaplicarFocoDiferido(hwnd);
                ArrancarClavado();                 // ← red de seguridad de posición
                textoEstado.Text = "Proyecto lanzado y adoptado.";
            }
            else textoEstado.Text = "Apareció la ventana pero falló la adopción.";
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

        // "+" — Capa 2: abrirá el selector de programas para adoptar/lanzar.
        private void botonAgregar_Click(object sender, RoutedEventArgs e)
        {
            textoEstado.Text = "Agregar ventana: próximamente.";
        }

        // Plegar sidebar — Capa 2: plegado real a riel de 56px.
        private void botonPlegar_Click(object sender, RoutedEventArgs e)
        {
            textoEstado.Text = "Plegar panel: próximamente.";
        }

        // Navegación de espacios — Capa 2: cambiar la vista central.
        private void navCockpit_Click(object sender, RoutedEventArgs e) { }
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