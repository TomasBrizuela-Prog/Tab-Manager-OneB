using System;
using System.ComponentModel;
using OneBlack.Core;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Representa una pestaña de la UI: una ventana adoptada, viva. Es el "dato"
    /// que la barra de pestañas dibuja. Implementa INotifyPropertyChanged para que
    /// la UI se actualice sola cuando cambia algo (ej: cuál está activa) — el mismo
    /// concepto que el binding reactivo de Angular.
    /// </summary>
    public class PestañaVentana : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; }
        public ProgramaSoportado Programa { get; }
        public string Titulo { get; }   // qué mostrar en la pestaña (ej: "RECUPERATORIO")

        public string Carpeta { get; }
        public PestañaVentana(IntPtr hwnd, ProgramaSoportado programa, string titulo, string carpeta)
        {
            Hwnd = hwnd;
            Programa = programa;
            Titulo = titulo;
            Carpeta = carpeta;
        }

        // Si esta pestaña es la que está mostrándose ahora. La UI la resalta según esto.
        private bool estaActiva;
        public bool EstaActiva
        {
            get => estaActiva;
            set { estaActiva = value; Notificar(nameof(EstaActiva)); }
        }

        // Boilerplate de INotifyPropertyChanged: avisa a la UI que una propiedad cambió.
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notificar(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}