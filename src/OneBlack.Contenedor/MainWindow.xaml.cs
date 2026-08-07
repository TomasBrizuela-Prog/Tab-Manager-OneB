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

       

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        public MainWindow()
        {
            InitializeComponent();
            LanzarJanitor();
            RefrescarCandidatas();

            // Cuando el hueco cambia de tamaño, reajustar las ventanas adoptadas.
            anfitriona.SizeChanged += (s, e) =>
            {
                int ancho = (int)anfitriona.ActualWidth;
                int alto = (int)anfitriona.ActualHeight;
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
            var candidatas = buscador.BuscarCandidatas();

            listaCandidatas.ItemsSource = candidatas;
            if (candidatas.Count > 0)
                listaCandidatas.SelectedIndex = 0;

            textoEstado.Text = $"{candidatas.Count} ventana(s) adoptable(s).";
        }

        private void botonAdoptar_Click(object sender, RoutedEventArgs e)
        {
            // La candidata elegida en el desplegable.
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
            int ancho = (int)anfitriona.ActualWidth;
            int alto = (int)anfitriona.ActualHeight;

            bool ok = adoptador.Adoptar(elegida.Hwnd, hwndContenedor, ancho, alto);
            if (ok)
            {
                adoptador.MostrarSolo(elegida.Hwnd);  // mostrar la recién adoptada, ocultar el resto
                textoEstado.Text = $"{elegida.Programa.NombreMostrado} adoptado.";
            }
            else
            {
                textoEstado.Text = "Falló la adopción.";
            }
        }

        private void botonDevolver_Click(object sender, RoutedEventArgs e)
        {
            // La candidata elegida es la que devolvemos (si está adoptada).
            if (listaCandidatas.SelectedItem is not VentanaCandidata elegida)
            {
                textoEstado.Text = "Elegí la ventana a devolver.";
                return;
            }

            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            bool ok = adoptador.Devolver(elegida.Hwnd, hwndContenedor);
            textoEstado.Text = ok
                ? $"{elegida.Programa.NombreMostrado} devuelto."
                : "Esa ventana no estaba adoptada.";
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