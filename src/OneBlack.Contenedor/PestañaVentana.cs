using System;
using System.ComponentModel;
using OneBlack.Core;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Una pestaña de la UI. Puede estar VACÍA (recién creada con "+", muestra la
    /// página de nueva pestaña, sin ventana adoptada) u OCUPADA (tiene un IDE
    /// adoptado). Nace vacía y se transforma en ocupada al adoptar algo en ella
    /// —como una pestaña de navegador que pasa de "nueva pestaña" a un sitio—.
    /// </summary>
    public class PestañaVentana : INotifyPropertyChanged
    {
        // Estado OCUPADA: hay una ventana adoptada. IntPtr.Zero = pestaña VACÍA.
        private IntPtr hwnd = IntPtr.Zero;
        public IntPtr Hwnd
        {
            get => hwnd;
            private set { hwnd = value; Notificar(nameof(Hwnd)); Notificar(nameof(EstaVacia)); }
        }

        // Datos del contenido adoptado (null mientras está vacía).
        public ProgramaSoportado? Programa { get; private set; }
        public string? Carpeta { get; private set; }

        // ¿La pestaña todavía no tiene nada adoptado? (muestra la página nueva pestaña)
        public bool EstaVacia => hwnd == IntPtr.Zero;

        // Título mostrado: "Nueva pestaña" mientras está vacía; el del proyecto al ocuparse.
        private string titulo = "Nueva pestaña";

        // Color identificador de la pestaña (el puntito). HOY: rotativo por pestaña.
        // FUTURO: será el color del PROYECTO al que pertenece (mismo proyecto =
        // mismo color, aunque sean carpetas distintas). Por eso vive acá como dato:
        // cuando exista el sistema de proyectos, solo cambia QUIÉN lo asigna, no esto.
        private string color = "#58D5CF";   // valor por defecto (cian), se sobreescribe al ocupar
        public string Color
        {
            get => color;
            set { color = value; Notificar(nameof(Color)); }
        }
        public string Titulo
        {
            get => titulo;
            private set { titulo = value; Notificar(nameof(Titulo)); }
        }

        // Si es la pestaña que se está mostrando ahora.
        private bool estaActiva;
        public bool EstaActiva
        {
            get => estaActiva;
            set { estaActiva = value; Notificar(nameof(EstaActiva)); }
        }

        /// <summary>Crea una pestaña VACÍA (nueva pestaña sin contenido).</summary>
        public PestañaVentana() { }

        /// <summary>
        /// Transforma una pestaña vacía en OCUPADA: le asigna la ventana adoptada,
        /// el programa, la carpeta y el título. Es la transición "nueva pestaña → IDE".
        /// </summary>
        public void Ocupar(IntPtr hwndAdoptado, ProgramaSoportado programa, string? carpeta, string titulo, string color)
        {
            Programa = programa;
            Carpeta = carpeta;
            Titulo = titulo;
            Color = color;
            Hwnd = hwndAdoptado;   // setear esto último dispara EstaVacia = false
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notificar(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}