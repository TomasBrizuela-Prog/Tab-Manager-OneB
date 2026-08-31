using System;
using System.Collections.Generic;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Un proyecto guardado: un grupo de carpetas que se abren juntas, con nombre y
    /// color propios. Es la unidad que el usuario arma una vez y después lanza con un
    /// botón. El color lo comparten todas sus pestañas (identidad visual del proyecto).
    /// </summary>
    public class Proyecto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();   // identifica el proyecto
        public string Nombre { get; set; } = "";
        public string Color { get; set; } = "#58D5CF";
        public List<CarpetaProyecto> Carpetas { get; set; } = new();
        public DateTime UltimoUso { get; set; } = DateTime.Now;       // para ordenar recientes
    }

    /// <summary>
    /// Una carpeta dentro de un proyecto, con el IDE que la abre (elegido por el usuario).
    /// ProgramaId es la clave estable del programa en el catálogo, para resolver el
    /// ProgramaSoportado al abrir el proyecto.
    /// </summary>
    public class CarpetaProyecto
    {
        public string Ruta { get; set; } = "";
        public string ProgramaId { get; set; } = "";
    }
}