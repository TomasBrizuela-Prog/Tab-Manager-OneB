using Microsoft.Win32;
using OneBlack.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Diálogo modal para dar de alta un proyecto: el usuario agrega carpetas (y elige
    /// con qué IDE abre cada una), le pone nombre y elige un color. Al guardar deja el
    /// resultado en la propiedad Resultado; si cancela, queda null.
    /// </summary>
    public partial class VentanaNuevoProyecto : Window
    {
        // Solo ofrecemos programas que aceptan carpeta: un proyecto agrupa carpetas,
        // así que un programa sin carpeta (ej. Claude) no tiene sentido acá.
        private readonly IReadOnlyList<ProgramaSoportado> programasConCarpeta =
            CatalogoDeProgramas.Todos().Where(p => p.UsaCarpeta).ToList();

        private readonly ObservableCollection<FilaCarpeta> carpetas = new();
        private readonly ObservableCollection<SwatchColor> colores = new();

        // El proyecto armado. null hasta que el usuario guarda con éxito.
        public Proyecto? Resultado { get; private set; }

        public VentanaNuevoProyecto()
        {
            InitializeComponent();

            listaCarpetas.ItemsSource = carpetas;
            carpetas.CollectionChanged += (s, e) => ActualizarVacio();
            ActualizarVacio();

            // Cargar los swatches desde la paleta compartida; el primero, seleccionado.
            foreach (var hex in PaletaProyectos.Colores)
                colores.Add(new SwatchColor(hex));
            colores[0].Seleccionado = true;
            listaColores.ItemsSource = colores;
        }

        // Muestra el cartel "todavía no agregaste carpetas" solo cuando la lista está vacía.
        private void ActualizarVacio() =>
            txtVacio.Visibility = carpetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void AgregarCarpeta_Click(object sender, RoutedEventArgs e)
        {
            // OpenFolderDialog viene en .NET 8+. Multiselect permite agregar varias de una.
            var dialogo = new OpenFolderDialog
            {
                Title = "Elegí una o más carpetas del proyecto",
                Multiselect = true
            };
            if (dialogo.ShowDialog() != true) return;

            foreach (var ruta in dialogo.FolderNames)
            {
                // Evitar duplicar una carpeta ya agregada.
                if (carpetas.Any(c => string.Equals(c.Ruta, ruta, StringComparison.OrdinalIgnoreCase)))
                    continue;
                carpetas.Add(new FilaCarpeta(ruta, programasConCarpeta));
            }
        }

        private void QuitarCarpeta_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FilaCarpeta fila)
                carpetas.Remove(fila);
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not SwatchColor elegido) return;
            foreach (var c in colores)
                c.Seleccionado = (c == elegido);
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e) => Close();

        private void Guardar_Click(object sender, RoutedEventArgs e)
        {
            string nombre = txtNombre.Text.Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                MessageBox.Show("Poné un nombre al proyecto.", "Falta el nombre",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (carpetas.Count == 0)
            {
                MessageBox.Show("Agregá al menos una carpeta.", "Faltan carpetas",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Resultado = new Proyecto
            {
                Nombre = nombre,
                Color = colores.First(c => c.Seleccionado).Hex,
                Carpetas = carpetas.Select(c => new CarpetaProyecto
                {
                    Ruta = c.Ruta,
                    ProgramaId = c.ProgramaSeleccionado.NombreProceso
                }).ToList()
            };
            Close();
        }
    }

    /// <summary>Fila editable del alta: una carpeta + el IDE elegido para abrirla.</summary>
    public class FilaCarpeta
    {
        public string Ruta { get; }
        public IReadOnlyList<ProgramaSoportado> Programas { get; }
        public ProgramaSoportado ProgramaSeleccionado { get; set; }

        public FilaCarpeta(string ruta, IReadOnlyList<ProgramaSoportado> programas)
        {
            Ruta = ruta;
            Programas = programas;
            ProgramaSeleccionado = programas.First();   // por defecto, el primero del catálogo
        }
    }

    /// <summary>Swatch del selector de color, con estado seleccionado (para el borde/centro).</summary>
    public class SwatchColor : INotifyPropertyChanged
    {
        public string Hex { get; }

        private bool seleccionado;
        public bool Seleccionado
        {
            get => seleccionado;
            set { seleccionado = value; PropertyChanged?.Invoke(this, new(nameof(Seleccionado))); }
        }

        public SwatchColor(string hex) => Hex = hex;
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}