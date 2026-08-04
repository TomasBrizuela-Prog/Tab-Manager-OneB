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