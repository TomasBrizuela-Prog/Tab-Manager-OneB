using System;
using System.Windows;
using OneBlack.Core;

namespace OneBlack.Contenedor
{
    public partial class MainWindow : Window
    {
        private readonly AdoptadorDeVentanas adoptador = new AdoptadorDeVentanas();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void botonAdoptar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Buscar la ventana de Notepad.
            IntPtr hwndNotepad = adoptador.BuscarNotepad();
            if (hwndNotepad == IntPtr.Zero)
            {
                textoEstado.Text = "No encontré Notepad. ¿Está abierto?";
                return;
            }

            // 2. Pedirle a la anfitriona el HWND de su contenedor (el hueco).
            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            if (hwndContenedor == IntPtr.Zero)
            {
                textoEstado.Text = "El contenedor todavía no está listo.";
                return;
            }

            // 3. Adoptar: meter Notepad en el hueco, del tamaño de la anfitriona.
            int ancho = (int)anfitriona.ActualWidth;
            int alto = (int)anfitriona.ActualHeight;
            bool ok = adoptador.Adoptar(hwndNotepad, hwndContenedor, ancho, alto);

            textoEstado.Text = ok
                ? $"Notepad adoptado (HWND {hwndNotepad})."
                : "Falló la adopción.";
        }

        private void botonDevolver_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwndContenedor = anfitriona.ObtenerHwndContenedor();
            bool ok = adoptador.Devolver(hwndContenedor);
            textoEstado.Text = ok ? "Notepad devuelto a su estado original." : "Nada que devolver.";
        }
    }
}