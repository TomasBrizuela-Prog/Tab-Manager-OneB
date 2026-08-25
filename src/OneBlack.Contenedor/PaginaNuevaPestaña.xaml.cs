using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OneBlack.Core;

namespace OneBlack.Contenedor
{
    public partial class PaginaNuevaPestaña : UserControl
    {
        /// <summary>
        /// Se dispara cuando el usuario eligió qué abrir (programa + carpeta).
        /// MainWindow lo escucha para lanzar+adoptar en la pestaña actual.
        /// </summary>
        public event Action<ProgramaSoportado, string?>? SeEligioAbrir;

        public PaginaNuevaPestaña()
        {
            InitializeComponent();
            // Llenar la lista con los programas del catálogo.
            listaApps.ItemsSource = CatalogoDeProgramas.Todos();
        }

        private void App_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ProgramaSoportado programa)
                return;

            string? carpeta = null;

            // Si el programa usa carpeta, pedirla con el selector.
            if (programa.UsaCarpeta)
            {
                var dialogo = new OpenFolderDialog { Title = "Elegí la carpeta del proyecto" };
                if (dialogo.ShowDialog() != true)
                    return;   // canceló
                carpeta = dialogo.FolderName;
            }

            txtInfo.Text = $"Abriendo {programa.NombreMostrado}…";
            SeEligioAbrir?.Invoke(programa, carpeta);
        }
    }
}