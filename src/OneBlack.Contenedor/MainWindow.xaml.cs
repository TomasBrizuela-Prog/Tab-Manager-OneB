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

        // Timer de la salvaguarda de repintado. Lo guardamos para poder frenarlo:
        // no queremos que siga disparando tras un Devolver, ni que se apilen timers
        // de adopciones distintas.
        private System.Windows.Threading.DispatcherTimer? repintadoTimer;

        public MainWindow()
        {
            InitializeComponent();
            //LanzarJanitor();
            //RefrescarCandidatas();

            // Cuando el hueco cambia de tamaño, reajustar las ventanas adoptadas.
            anfitriona.SizeChanged += (s, e) =>
            {
                var (ancho, alto) = DimensionesFisicas();
                adoptador.ReajustarTamaño(ancho, alto);
            };
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
        private void botonRefrescar_Click(object sender, RoutedEventArgs e)
        {
            RefrescarCandidatas();
        }

        private void listaCandidatas_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Al cambiar la selección, si esa ventana está adoptada, mostrarla
            // y ocultar las demás. Si no está adoptada, no hacemos nada (aún).
            if (listaCandidatas.SelectedItem is VentanaCandidata elegida
                && adoptador.YaEstaAdoptada(elegida.Hwnd))
            {
                adoptador.MostrarSolo(elegida.Hwnd);
                textoEstado.Text = $"Mostrando {elegida.Programa.NombreMostrado}.";
            }
        }

        private void RefrescarCandidatas()
        {
            var buscador = new BuscadorDeVentanas();
            var libres = buscador.BuscarCandidatas();

            // Combinar: las ventanas libres (recién enumeradas) + las que ya adoptamos
            // (que no aparecen en la enumeración porque son hijas de OneBlack).
            // Evitamos duplicados por HWND.
            var todas = new List<VentanaCandidata>(candidatasAdoptadas);
            foreach (var libre in libres)
            {
                if (!todas.Any(c => c.Hwnd == libre.Hwnd))
                    todas.Add(libre);
            }

            // Preservar la selección actual para no romper la ventana visible.
            var seleccionActual = listaCandidatas.SelectedItem as VentanaCandidata;

            listaCandidatas.ItemsSource = todas;

            // Restaurar la selección si la ventana sigue en la lista.
            if (seleccionActual != null)
            {
                var reencontrada = todas.FirstOrDefault(c => c.Hwnd == seleccionActual.Hwnd);
                if (reencontrada != null)
                    listaCandidatas.SelectedItem = reencontrada;
            }

            textoEstado.Text = $"{todas.Count} ventana(s): {candidatasAdoptadas.Count} adoptada(s), {libres.Count} libre(s).";
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

        private void botonAdoptar_Click(object sender, RoutedEventArgs e)
        {
            if (listaCandidatas.SelectedItem is not VentanaCandidata elegida)
            {
                textoEstado.Text = "Elegí una ventana de la lista primero.";
                return;
            }

            if (adoptador.YaEstaAdoptada(elegida.Hwnd))
            {
                textoEstado.Text = "Esa ventana ya está adoptada.";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            var (ancho, alto) = DimensionesFisicas();

            bool ok = adoptador.Adoptar(elegida.Hwnd, hwndContenedor, ancho, alto);
            if (ok)
            {
                candidatasAdoptadas.Add(elegida);
                adoptador.MostrarSolo(elegida.Hwnd);

                // Salvaguarda de dibujado (VS Code / carga alta): unos repintados espaciados.
                ProgramarRepintados(elegida.Hwnd);

                // El encaje correcto solo ocurre cuando el layout está 100% asentado
                // (el SizeChanged encaja bien porque ahí el tamaño ya es final; al adoptar,
                // no). Por eso diferimos a la fase Render, cuando las dimensiones son finales.
                //
                // GUARDAMOS la operación para poder cancelarla si Devolver corre antes:
                // esa carrera (adoptar+devolver rápido) era la que corrompía la ventana.
                encajeDiferido = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var (a, al) = DimensionesFisicas();
                    adoptador.ReajustarTamaño(a, al);
                    encajeDiferido = null;   // ya corrió, no hay nada que cancelar
                }), System.Windows.Threading.DispatcherPriority.Render);

                textoEstado.Text = $"{elegida.Programa.NombreMostrado} adoptado.";
            }
            else
            {
                textoEstado.Text = "Falló la adopción.";
            }
        }
        private void botonDevolver_Click(object sender, RoutedEventArgs e)
        {
            if (listaCandidatas.SelectedItem is not VentanaCandidata elegida)
            {
                textoEstado.Text = "Elegí la ventana a devolver.";
                return;
            }

            // CLAVE: si hay un encaje diferido pendiente de un Adoptar reciente, lo
            // CANCELAMOS antes de devolver. Si corriera después del Devolver, haría
            // SetWindowPos sobre una ventana ya restaurada → corrupción. Esta línea
            // es la que cierra la condición de carrera de raíz.
            if (encajeDiferido is { Status: System.Windows.Threading.DispatcherOperationStatus.Pending })
            {
                encajeDiferido.Abort();
                repintadoTimer?.Stop();
                encajeDiferido = null;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            bool ok = adoptador.Devolver(elegida.Hwnd, hwndContenedor);
            if (ok)
            {
                candidatasAdoptadas.RemoveAll(c => c.Hwnd == elegida.Hwnd);
                anfitriona.InvalidateVisual();
                textoEstado.Text = $"{elegida.Programa.NombreMostrado} devuelto.";
            }
            else
            {
                textoEstado.Text = "Esa ventana no estaba adoptada.";
            }
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
    }
}