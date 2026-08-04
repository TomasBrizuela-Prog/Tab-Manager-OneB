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

        // Recordamos el HWND de la ventana que adoptamos, para poder devolverla.
        // (En el spike manejamos una sola; el adoptador ya soporta varias.)
        private IntPtr hwndNotepadAdoptada = IntPtr.Zero;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        public MainWindow()
        {
            InitializeComponent();
            LanzarJanitor();
        }
        private void LanzarJanitor()
        {
            try
            {
                // El PID de nuestro propio proceso (OneBlack), que el janitor va a vigilar.
                int miPid = Process.GetCurrentProcess().Id;

                // Ruta al .exe del janitor. Asumimos que está en la misma carpeta de salida
                // que OneBlack (los dos proyectos compilan al mismo Debug/net10.0-windows
                // cuando corren juntos... pero OJO: en realidad cada proyecto tiene SU carpeta.
                // Para el spike, construimos la ruta relativa al ejecutable del janitor.)
                string carpetaBase = AppDomain.CurrentDomain.BaseDirectory;
                string rutaJanitor = Path.Combine(carpetaBase,
                    "..", "..", "..", "..", "..",
                    "src", "OneBlack.Janitor", "bin", "Debug", "net10.0-windows",
                    "OneBlack.Janitor.exe");

                var inicio = new ProcessStartInfo
                {
                    FileName = rutaJanitor,
                    Arguments = miPid.ToString(),
                    UseShellExecute = false,
                    CreateNoWindow = true   // el janitor corre invisible, sin ventana
                };

                Process.Start(inicio);
                textoEstado.Text = "Janitor lanzado. Vigilando.";
            }
            catch (Exception ex)
            {
                textoEstado.Text = $"No pude lanzar el janitor: {ex.Message}";
            }
        }
        private void botonAdoptar_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwndNotepad = adoptador.BuscarNotepad();
            if (hwndNotepad == IntPtr.Zero)
            {
                textoEstado.Text = "No encontré Notepad. ¿Está abierto?";
                return;
            }

            // Guarda contra doble adopción: si ya la tenemos, avisamos y salimos.
            if (adoptador.YaEstaAdoptada(hwndNotepad))
            {
                textoEstado.Text = "Ese Notepad ya está adoptado.";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            if (hwndContenedor == IntPtr.Zero)
            {
                textoEstado.Text = "El contenedor todavía no está listo.";
                return;
            }

            int ancho = (int)anfitriona.ActualWidth;
            int alto = (int)anfitriona.ActualHeight;
            bool ok = adoptador.Adoptar(hwndNotepad, hwndContenedor, ancho, alto);

            if (ok)
            {
                hwndNotepadAdoptada = hwndNotepad;
                textoEstado.Text = $"Notepad adoptado (HWND {hwndNotepad}).";
            }
            else
            {
                textoEstado.Text = "Falló la adopción.";
            }
            throw new Exception("Crasheo");
        }

        private void botonDevolver_Click(object sender, RoutedEventArgs e)
        {
            if (hwndNotepadAdoptada == IntPtr.Zero)
            {
                textoEstado.Text = "Nada que devolver.";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            bool ok = adoptador.Devolver(hwndNotepadAdoptada, hwndContenedor);

            if (ok)
            {
                hwndNotepadAdoptada = IntPtr.Zero;
                textoEstado.Text = "Notepad devuelto a su estado original.";
            }
            else
            {
                textoEstado.Text = "Nada que devolver.";
            }
        }
        private void botonListar_Click(object sender, RoutedEventArgs e)
        {
            var buscador = new BuscadorDeVentanas();
            var candidatas = buscador.BuscarCandidatas();

            // Por ahora, mostramos la lista en un MessageBox para verla con los ojos.
            var texto = new System.Text.StringBuilder();
            texto.AppendLine($"Encontré {candidatas.Count} ventanas candidatas:\n");
            foreach (var c in candidatas)
                texto.AppendLine(c.ToString());

            MessageBox.Show(texto.ToString(), "Ventanas adoptables");
        }
        private void botonAdoptarVSCode_Click(object sender, RoutedEventArgs e)
        {
            // Buscar entre las candidatas la primera que sea VS Code.
            var buscador = new BuscadorDeVentanas();
            var candidatas = buscador.BuscarCandidatas();

            var vscode = candidatas.FirstOrDefault(c =>
                c.NombreProceso.Equals("Code", StringComparison.OrdinalIgnoreCase));

            if (vscode == null)
            {
                textoEstado.Text = "No encontré VS Code abierto.";
                return;
            }

            if (adoptador.YaEstaAdoptada(vscode.Hwnd))
            {
                textoEstado.Text = "Ese VS Code ya está adoptado.";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            int ancho = (int)anfitriona.ActualWidth;
            int alto = (int)anfitriona.ActualHeight;

            bool ok = adoptador.Adoptar(vscode.Hwnd, hwndContenedor, ancho, alto);

            if (ok)
            {
                hwndNotepadAdoptada = vscode.Hwnd;

                // SACUDÓN FUERTE: minimizar y restaurar fuerza a Chromium a reconstruir
                // su superficie de render por completo. 6 = minimizar, 9 = restaurar.
                const int SW_MINIMIZE = 6;
                const int SW_RESTORE = 9;
                ShowWindow(vscode.Hwnd, SW_MINIMIZE);
                ShowWindow(vscode.Hwnd, SW_RESTORE);
                MoveWindow(vscode.Hwnd, 0, 0, ancho, alto, true);

                textoEstado.Text = $"VS Code adoptado (HWND {vscode.Hwnd}).";
            }
            else
            {
                textoEstado.Text = "Falló la adopción de VS Code.";
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
    }
}