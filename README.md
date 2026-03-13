# SpecimenFX17 Viewer — Visor BLI hiperespectral (documentación completa)

Este repositorio contiene una aplicación de escritorio (Windows, .NET 10)
diseñada para leer, visualizar y exportar imágenes hiperespectrales generadas
por el equipo SpecimenFX en formato ENVI (.hdr + .raw). Su objetivo es
facilitar la inspección visual (imágenes BLI en pseudocolor), el análisis
espectral de píxeles individuales y la exportación de resultados para
procesado posterior.

Este README describe en detalle todas las funcionalidades disponibles,
cómo se implementan y cómo usar la aplicación tanto desde la interfaz gráfica
como programáticamente.

---

## Índice rápido

- Funcionalidades principales
- Requisitos e instalación
- Uso desde la interfaz gráfica (descripción detallada)
- Pipeline de procesamiento y algoritmos (normalización, gamma, mapas de color)
- Formatos y lectura de datos (ENVI .hdr/.raw: interleave, data types, byte order)
- API pública y ejemplos de código
- Exportación y opciones de guardado
- Rendimiento, memoria y optimizaciones
- Tests y cómo ejecutar
- Contribuir, licencia y contacto

---

## Funcionalidades principales (resumen)

- Carga de imágenes hiperespectrales ENVI (.hdr + .raw) con soporte para
  interleave BSQ / BIL / BIP y múltiples tipos de datos (float32, uint16,
  int16, double, byte).
- Visualización interactiva de una banda como imagen BLI (pseudocolor) con
  diferentes paletas, barra de escala y modo grises.
- Control fino de contraste mediante percentiles bajos/altos, umbral de ruido
  y corrección gamma.
- Selección de hasta 6 píxeles sobre la imagen con visualización comparativa
  de sus espectros (gráfico interactivo con marcadores de la banda visible).
- Exportación de la banda actual o de todas las bandas a PNG/TIFF/BMP con
  metadatos y opciones de compresión (si el formato lo soporta).
- Renderizado de combinaciones RGB (tres bandas asignadas a R/G/B) y guardado
  como imagen compuesta.
- API programática (clases HyperspectralCube, EnviHeader, BliRenderer,
  BliRenderOptions) para integrar lectura y renderizado en otros proyectos.
- Soporte para abrir archivos con extensiones alternativas (.img, .dat, .bil,
  .bip, .bsq) si el `.raw` no se encuentra.

---

## Requisitos e instalación

- Sistema operativo: Windows 10 o superior.
- .NET SDK 10 instalado: https://dotnet.microsoft.com/es-es/download/dotnet
- Visual Studio 2022/2024/2026 (o Rider) recomendado para desarrollo.

Clonar el repositorio:

1. git clone https://github.com/KACRalberto/Visor-de-imagenes-hiperespectrales-capturadas-por-SpecimenFx17.git
2. Abrir la solución o el proyecto `SpecimenFX17Viewer.csproj` en Visual Studio.
3. Compilar y ejecutar (F5 o ▶️).

Consejo: Para carpetas con archivos grandes (.raw), asegúrate de tener espacio
en disco suficiente y no poner los datos en OneDrive/Dropbox con sincronización
activa durante la lectura para evitar efectos de I/O.

---

## Uso desde la interfaz gráfica — Descripción completa

La ventana principal está dividida en tres bloques principales: panel central
de imagen, gráfico espectral y panel lateral de controles.

1) Panel de imagen (izquierda superior)
   - Visualiza la banda actualmente seleccionada en la paleta elegida.
   - Información mostrada en tiempo real: coordenadas del cursor (X,Y), banda
     actual, longitud de onda y valor numérico del píxel.
   - Interacciones:
     - Slider de banda: recorre bandas; la línea vertical en el gráfico
       espectral se actualiza para marcar la λ activa.
     - Clic izquierdo: añade un punto de interés (hasta 6). Cada punto pinta
       una cruz de color y su espectro se muestra en el gráfico.
     - Doble clic: borra todos los puntos seleccionados.
     - Zoom (rueda del ratón + Ctrl): acercar/alejar la vista (si está activo).

2) Gráfico espectral (izquierda inferior)
   - Eje X: longitudes de onda (según `wavelength` en `.hdr`) o índices de
     banda si no hay wavelengths.
   - Eje Y: valores absolutos del `.raw` (reflectancia, radiancia o cuentas).
   - Cada punto seleccionado genera:
     - Línea de color única y área sombreada bajo la curva.
     - Punto resaltado en la banda actualmente visible.
     - Leyenda con coordenadas y estadísticas (mín, máx, integral aproximada).
   - Herramientas de análisis:
     - Suavizado (opcional, moving average o Savitzky-Golay) para ver la
       forma espectral sin ruido.
     - Promediado espacial: calcular espectro medio en ventana NxN alrededor
       del píxel seleccionado.

3) Panel lateral (derecha) — Controles principales
   - Visualización
     - Selector de paleta (Rainbow, HeatMap, ColdBlue, GreenFluorescent,
       RedFluorescent, VisibleSpectrum, Grayscale). Añadir nuevas paletas es
       posible implementando un IColormap y registrándolo en BliRenderer.
     - Mostrar/Ocultar barra de escala (colorbar) con unidades y ticks.
     - Modo Grayscale (forzar visualización en escala de grises).
   - Ajustes de imagen
     - Gamma (float): corrección no lineal aplicada tras la normalización.
     - Percentil bajo / alto (0–100): define el rango de contraste basado en
       percentiles de píxeles (por defecto 2 / 98).
     - Umbral de señal (float): valores por debajo se pintan de negro.
     - Normalización por banda o global (usar percentiles por banda o por
       cubo completo).
   - Exportar
     - Exportar banda actual (PNG/TIFF/BMP). Opciones: incluir colorbar,
       formato de color (24bpp/32bpp), metadatos (banda, λ, percentiles usados).
     - Exportar todas las bandas: guarda cada banda como archivo separado y
       opcionalmente genera un script para animación (FFmpeg) o un GIF.
     - Exportar espectros seleccionados como CSV (columnas: wavelength, val1,
       val2, ...).
   - Info de banda
     - Muestra número de banda, λ asociada, min/max y valor medio.

---

## Pipeline de procesamiento y algoritmos (detallado)

El renderizado de una banda a BLI sigue estos pasos en `BliRenderer`:

1) Extracción de la banda (float[,])
   - Se obtiene una matriz 2D con los valores de la banda solicitada. Si el
     cubo está almacenado en otro tipo numérico, se convierten a float para
     evitar pérdida de precisión en cálculos.

2) Normalización por percentiles
   - Se calculan los percentiles p_low y p_high del conjunto de píxeles.
   - Los valores se clipean y normalizan a [0,1] mediante:

     t = (v - P_low) / (P_high - P_low)

     Donde P_low = percentile(p_low), P_high = percentile(p_high). Valores
     fuera del rango se saturan a 0 o 1.

   - Este método es robusto frente a outliers y saturación.

3) Aplicación de umbral de señal
   - Los valores normalizados menores que el umbral (en unidades del raw o en
     t) se establecen a 0 (negro). Esto elimina ruido de fondo.

4) Corrección gamma
   - Aplicamos la función y = x^(1/gamma) (o x^gamma según convención). En
     este proyecto se usa y = x^(1/gamma) para que gamma < 1 ilumine zonas
     bajas.

5) Mapeo de color
   - Para cada valor t en [0,1] se consulta la paleta (IColormap) que devuelve
     un Color RGBA.
   - En paletas con componente alfa se respeta la transparencia; al final se
     compone sobre fondo negro.

6) Opciones adicionales
   - Escalado por factor espacial, suavizado gaussian o median filters antes
     del render para reducir ruido.

Fórmulas clave:

- Normalización por percentiles: t = clamp((v - P_low)/(P_high-P_low), 0, 1)
- Gamma (usada aquí): out = pow(t, 1.0f / gamma)

Nota: Cuando se renderizan imágenes para análisis cuantitativo, es recomendable
usar percentiles 0–100 y gamma = 1 para evitar transformaciones no lineales.

---

## Formatos y lectura de datos ENVI (.hdr + .raw)

El formato ENVI comunica cómo interpretar el `.raw` desde el archivo `.hdr`.
Campos importantes y cómo se usan en `EnviHeader.cs`:

- samples = cantidad de columnas (width)
- lines   = cantidad de filas (height)
- bands   = número de bandas (depth)
- data type = código numérico ENVI (1,2,4,5,12, ...)
- interleave = BSQ | BIL | BIP
- byte order = 0 (little-endian) | 1 (big-endian)
- wavelength / wavelengths = lista o rango de longitudes de onda asociadas
  a cada banda.

Lectura del `.raw`:

- Si interleave=BSQ: las bandas están contiguas en el archivo. Lectura sencilla
  por bloques.
- Si interleave=BIL: cada línea contiene los valores de todas las bandas para
  esa fila; la lectura accede por filas y reasigna a la estructura interna.
- Si interleave=BIP: cada píxel contiene las N bandas en secuencia; lectura
  por pixel y reordenado.

Para tipos de dato se mapean a C# así (ejemplos):

- ENVI 1  -> byte
- ENVI 2  -> short (Int16)
- ENVI 4  -> float (Single)
- ENVI 5  -> double
- ENVI 12 -> ushort (UInt16)

Byte order: si el sistema del archivo difiere, se aplica Endian swap al leer.

Memory layout utilizado por la aplicación (interno):

- float[banda, fila, columna] (orden por banda para acceso a bandas contiguas)

Carga en memoria vs streaming:

- Para cubos pequeños/medios se carga todo en memoria para acceso interactivo.
- Para cubos muy grandes se pueden implementar lecturas parciales (tiling)
  o mapeo de archivo (MemoryMappedFile). Actualmente la aplicación está
  optimizada para sistemas con suficiente RAM para el cubo completo.

---

## API pública (clases y ejemplos)

Principales clases y miembros (resumen):

- EnviHeader
  - Propiedades: Samples, Lines, Bands, DataType, Interleave, ByteOrder,
    Wavelengths (float[]), Metadata (Dictionary<string,string>)
  - static EnviHeader Parse(string hdrPath)

- HyperspectralCube
  - Propiedades: Header (EnviHeader), Samples, Lines, Bands
  - static HyperspectralCube Load(string hdrPath) — carga .hdr + .raw
  - float[] GetSpectrum(int line, int sample) — devuelve espectro (Bands)
  - float[,] GetBand(int bandIndex) — devuelve la banda como 2D
  - void SaveBandAsPng(int bandIndex, string path, BliRenderOptions opts)

- BliRenderer
  - static Bitmap RenderBand(HyperspectralCube cube, int bandIndex,
    BliRenderOptions opts)
  - static Bitmap RenderRGB(HyperspectralCube cube, int r, int g, int b,
    BliRenderOptions opts)

- BliRenderOptions
  - Colormap (enum / IColormap), Gamma, LowPercentile, HighPercentile,
    Threshold, DrawColorbar, Wavelength

Ejemplo de uso (conservando el estilo del proyecto):

using SpecimenFX17.Imaging;

// Cargar cubo
var cube = HyperspectralCube.Load(@"C:\datos\muestra.hdr");

// Renderizar banda cercana a 650 nm
int banda650 = cube.Header.Wavelengths
    .Select((wl, i) => (diff: Math.Abs(wl - 650.0), i))
    .OrderBy(x => x.diff).First().i;

var opts = new BliRenderOptions
{
    Colormap = BliColormap.RedFluorescent,
    Gamma = 0.8f,
    LowPercentile = 2f,
    HighPercentile = 98f,
    DrawColorbar = true,
    Wavelength = cube.Header.Wavelengths[banda650]
};

using var img = BliRenderer.RenderBand(cube, banda650, opts);
img.Save(@"C:\salida\bli_650nm.png", System.Drawing.Imaging.ImageFormat.Png);

También se puede obtener el espectro de un píxel:

float[] espectro = cube.GetSpectrum(line: 240, sample: 320);

Exportar varios espectros a CSV:

// ejemplo: escribir columna de wavelength + N columnas de valores

---

## Exportación y formatos soportados

- Imágenes: PNG (predeterminado), TIFF y BMP. PNG recomendado para calidad y
  compresión sin pérdida. TIFF útil para metadatos y bitdepth mayores.
- Espectros: CSV con encabezado (wavelength, x1y2, x2y3, ...).
- Exportar todas las bandas crea nombres con sufijo `_bandaNNN.png` y puede
  generar un archivo batch para crear animaciones.

Opciones de exportación incluyen la inclusión del colorbar y metadatos JSON
adjunto con los parámetros de render (percentiles, gamma, colormap).

---

## Rendimiento, memoria y optimizaciones

- Lectura: uso de buffering y BinaryReader para lectura rápida de raw.
- Conversión a float centralizada para evitar conversiones múltiples.
- Rendering: uso de unsafe/LockBits y manipulación directa de bytes del
  Bitmap para maximizar velocidad (evitar SetPixel por píxel).
- Multi-threading: el render de imagen puede paralelizarse por filas o
  tiles para usar múltiples cores; revisar BliRenderer si se desea activar.

Recomendaciones:
- Para cubos >8GB, usar MemoryMappedFile o procesado por tiles.
- Si la interfaz se vuelve lenta al calcular percentiles, calcularlos en un
  hilo en background y mostrar progreso.

---

## Tests y validación

Incluye tests unitarios (si están presentes en el proyecto): usar Test Explorer
o `dotnet test` para ejecutar. Las pruebas cubren parsing de .hdr, lectura de
raw en los tres interleaves y render básico.

Cómo ejecutar:

1. Abrir la solución en Visual Studio y ejecutar pruebas desde Test Explorer.
2. O bien usar la CLI: dotnet test ./tests/SpecimenFX17.Tests

---

## Problemas conocidos y limitaciones

- No implementado streaming por defecto: la aplicación carga el cubo completo.
- No todos los formatos TIFF avanzados están soportados (p. ej. BigTIFF con
  compresión especial).
- El preprocesado avanzado (corrección radiométrica, calibración) no está
  integrado y debe realizarse por el usuario antes de la visualización si
  se requiere.

Si necesitas alguna de estas características, abre un issue para discutir
prioridades.

---

## Solución de problemas (detallada)

- Error al abrir / compilar:
  - Verifica que el .NET SDK 10 está instalado y que Visual Studio usa ese SDK.
  - Revisar las propiedades del proyecto y el TFM (TargetFramework) en
    `SpecimenFX17Viewer.csproj`.
- Archivo .raw no encontrado:
  - Asegúrate de que el nombre base del `.hdr` coincide con el `.raw` y que
    ambos están en la misma carpeta.
  - Si el `.raw` tiene extensión diferente, renómbralo o usa la opción de
    abrir archivo manual en la UI.
- Valores saturados (imagen muy clara o muy oscura):
  - Ajusta percentiles y gamma. Comprueba además si los valores en el raw son
    de tipo distinto (p. ej. 16 bits) y la interpretación de escala.

---

## Contribuir

Pasos recomendados:

1. Fork del repositorio.
2. Crear una rama con nombre descriptivo (`feature/...` o `fix/...`).
3. Añadir tests que cubran la nueva funcionalidad.
4. Abrir Pull Request y describir los cambios.

Si introduces cambios en la API pública, actualiza este README y añade ejemplos.


---

## Contacto y créditos

Desarrollador: KACR Alberto

Repositorio: https://github.com/KACRalberto/Visor-de-imagenes-hiperespectrales-capturadas-por-SpecimenFx17

Para bugs o solicitudes, abrir un issue en GitHub.

---

Última actualización: consultar el historial de commits para cambios recientes.
