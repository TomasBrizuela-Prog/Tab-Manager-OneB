using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Página de proyectos: lista los proyectos guardados (recientes primero) y deja
    /// crear uno nuevo, abrirlo o eliminarlo. No sabe NADA de repositorio ni de cómo se
    /// abren las ventanas: solo avisa vía eventos y MainWindow decide. Así la página es
    /// tonta y testeable, y la lógica vive en un solo lugar.
    /// </summary>
    public partial class PaginaProyectos : UserControl
    {
        public event Action? SePidioNuevoProyecto;
        public event Action<Proyecto>? SeEligioAbrir;
        public event Action<Proyecto>? SePidioEliminar;

        public PaginaProyectos()
        {
            InitializeComponent();
        }

        /// <summary>Recarga la lista con los proyectos dados (ya ordenados por recientes).</summary>
        public void Refrescar(IEnumerable<Proyecto> proyectos)
        {
            var lista = proyectos.ToList();
            // Reasignar ItemsSource fuerza el redibujo (la lista viene nueva del repositorio).
            listaProyectos.ItemsSource = null;
            listaProyectos.ItemsSource = lista;
            txtVacio.Visibility = lista.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Nuevo_Click(object sender, RoutedEventArgs e) =>
            SePidioNuevoProyecto?.Invoke();

        private void Abrir_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Proyecto p)
                SeEligioAbrir?.Invoke(p);
        }

        private void Eliminar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Proyecto p)
                SePidioEliminar?.Invoke(p);
        }
    }
}