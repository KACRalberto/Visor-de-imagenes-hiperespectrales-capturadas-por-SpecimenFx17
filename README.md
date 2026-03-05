# SpecimenFX17 — Visor BLI Hiperespectral
### Manual completo de uso y funcionamiento técnico

---

## ¿Qué hace este programa?

Este programa lee imágenes hiperespectrales del equipo **specimenFX17** (formato ENVI: archivos `.hdr` + `.raw`) y las visualiza como imágenes BLI (Bioluminescence Imaging) en pseudocolor. Además permite explorar el espectro de emisión de cualquier píxel de la imagen mediante gráficos lineales interactivos.

---

## Instalación y primer arranque

### Requisitos
- Windows 10 o superior
- .NET SDK 10 instalado (https://dotnet.microsoft.com/es-es/download/dotnet)
- Visual Studio 2022 Community (gratis)

### Pasos para abrir el proyecto
1. Coloca los 7 archivos del proyecto en una carpeta (por ejemplo `D:\PRACTICAS\HiperImg\`)
2. Abre Visual Studio
3. Clic en **"Abrir un proyecto o solución"**
4. Selecciona el archivo `SpecimenFX17Viewer.csproj`
5. Pulsa el botón verde ▶️ para compilar y ejecutar

---

## Estructura de archivos del proyecto

```
SpecimenFX17Viewer.csproj   → Archivo de proyecto (lo que abre Visual Studio)
EnviHeader.cs               → Lee y parsea el archivo .hdr
HyperspectralCube.cs        → Lee el binario .raw y construye el cubo de datos
BliRenderer.cs              → Convierte una banda en imagen BLI con pseudocolor
MainForm.cs                 → Ventana principal, interfaz gráfica e interacción
Program.cs                  → Punto de entrada (arranca la aplicación)
README.md                   → Este archivo
```

---

## Cómo funciona por dentro — El pipeline completo

### PASO 1 — Leer el archivo `.hdr` → `EnviHeader.cs`

El archivo `.hdr` es un texto plano que describe el contenido del `.raw`. Ejemplo real:

```
ENVI
samples     = 640       ← ancho de la imagen en píxeles
lines       = 480       ← alto de la imagen en píxeles
bands       = 128       ← número de bandas espectrales (longitudes de onda)
data type   = 4         ← tipo de número: 4 = float de 32 bits
interleave  = BIL       ← orden de los datos en el archivo binario
byte order  = 0         ← 0 = little-endian (procesadores Intel/AMD)
wavelength units = nm
wavelength = {
  400.0, 403.9, 407.8, ... , 900.0
}
```

`EnviHeader.cs` lee este archivo y extrae toda esa información para poder
interpretar correctamente el binario `.raw`.

**Tipos de dato soportados:**

| Código | Tipo         | Tamaño |
|--------|--------------|--------|
| 1      | Byte         | 1 byte |
| 2      | Entero 16bit | 2 bytes|
| 4      | Float 32bit  | 4 bytes|
| 5      | Double 64bit | 8 bytes|
| 12     | UInt16       | 2 bytes|

---

### PASO 2 — Leer el binario `.raw` → `HyperspectralCube.cs`

El archivo `.raw` contiene los números en bruto sin ninguna cabecera.
El orden en que están almacenados depende del campo `interleave` del `.hdr`:

```
BSQ (Band Sequential):
  Todos los píxeles de la banda 1
  Todos los píxeles de la banda 2
  ...
  → Ideal para extraer una sola banda completa

BIL (Band Interleaved by Line):  ← el más habitual en hiperspectral
  Fila 0 de la banda 1, Fila 0 de la banda 2, ..., Fila 0 de la banda N
  Fila 1 de la banda 1, Fila 1 de la banda 2, ..., Fila 1 de la banda N
  ...

BIP (Band Interleaved by Pixel):
  Píxel(0,0) banda 1, banda 2, ..., banda N
  Píxel(0,1) banda 1, banda 2, ..., banda N
  ...
  → Ideal para extraer el espectro de un píxel concreto
```

El resultado final es un **cubo tridimensional** en memoria:

```
float[banda, fila, columna]

Ejemplo:
  _cube[0, 240, 320]  → valor del píxel (320, 240) en la banda 0 (400 nm)
  _cube[64, 240, 320] → valor del píxel (320, 240) en la banda 64 (650 nm)
```

---

### PASO 3 — Convertir una banda a imagen BLI → `BliRenderer.cs`

Una vez que tenemos el cubo, para mostrar una banda como imagen BLI:

```
float[,] banda = cubo.GetBand(índice)    ← extrae la capa 2D
        │
        ▼
[1] Normalización por percentiles (2 % – 98 %)
    Esto elimina píxeles extremos (ruido, saturación) que distorsionarían
    el contraste. Un 2 % de píxeles muy brillantes no "apagará" el resto.
        │
        ▼
[2] Umbral de señal
    Valores por debajo del umbral → negro (fondo sin bioluminiscencia)
        │
        ▼
[3] Corrección gamma
    gamma < 1  → aclara las señales débiles (más sensible)
    gamma = 1  → escala lineal (por defecto)
    gamma > 1  → oscurece las señales débiles (más contraste en altas)
        │
        ▼
[4] Mapeo de color (colormap)
    El valor normalizado t ∈ [0, 1] se convierte en un color RGB
        │
        ▼
Bitmap 24bpp → se muestra en pantalla o se guarda en PNG/TIFF
```

**Paletas de color disponibles:**

| Paleta            | Descripción                                   | Cuándo usarla                    |
|-------------------|-----------------------------------------------|----------------------------------|
| Rainbow           | Negro→azul→cian→verde→amarillo→rojo→blanco    | BLI estándar, contraste máximo   |
| HeatMap           | Negro→rojo→naranja→amarillo→blanco            | Hotspots de bioluminiscencia     |
| ColdBlue          | Negro→azul→cian→blanco                        | Señales débiles, bajo ruido      |
| GreenFluorescent  | Negro→verde oscuro→verde brillante            | GFP, FITC, Alexa 488             |
| RedFluorescent    | Negro→rojo oscuro→rojo vivo                   | mCherry, Cy3, Alexa 594          |
| VisibleSpectrum   | Color físico según longitud de onda λ         | Visualización espectral real     |
| Grayscale         | Escala de grises                              | Análisis cuantitativo            |

---

## La interfaz — Qué hace cada parte

### Zona izquierda superior — La imagen

Aquí se muestra la imagen de la banda actualmente seleccionada en pseudocolor.

- **Slider superior:** desplázalo para cambiar de banda. Verás cómo cambia la imagen
  a medida que cambias la longitud de onda. La etiqueta muestra la banda actual y su λ.
- **Etiqueta flotante sobre la imagen:** al mover el ratón muestra en tiempo real
  las coordenadas del píxel (X, Y) y su valor numérico en la banda actual.
- **Clic izquierdo:** selecciona ese píxel para analizar su espectro (ver zona inferior).
  Puedes hacer clic en hasta 6 píxeles distintos para comparar sus espectros.
- **Doble clic:** limpia todos los píxeles seleccionados y el gráfico.

Los píxeles seleccionados se marcan con una **cruz de color** y sus coordenadas
directamente sobre la imagen.

---

### Zona izquierda inferior — El gráfico espectral

Este gráfico lineal muestra el **espectro completo** de los píxeles seleccionados.

```
Eje X = longitud de onda (en nm u otras unidades según el .hdr)
Eje Y = intensidad / reflectancia (el valor numérico del .raw)
```

**Qué información contiene el gráfico:**

- **Línea de color por cada píxel seleccionado:** cada color corresponde a la
  cruz del mismo color sobre la imagen. La leyenda en la esquina muestra las
  coordenadas de cada píxel.
- **Área sombreada bajo cada curva:** facilita ver la forma espectral de un vistazo.
- **Línea amarilla discontinua vertical:** marca la longitud de onda de la banda
  que está actualmente visible en la imagen. Al mover el slider, esta línea se mueve.
- **Punto sólido sobre cada curva:** indica el valor exacto de cada píxel en la
  banda actualmente visible.
- **Grid de fondo:** referencia visual para leer valores.

**Cómo comparar varios píxeles:**
1. Haz clic en un punto de la imagen → aparece la primera curva en cian
2. Haz clic en otro punto → aparece una segunda curva en amarillo
3. Sigue hasta 6 curvas simultáneas
4. Doble clic sobre la imagen (o botón "Limpiar puntos") para empezar de nuevo

---

### Panel derecho — Controles

**Sección VISUALIZACIÓN:**
- **Paleta de color:** elige cómo se colorea la imagen (ver tabla de paletas arriba)
- **Mostrar barra de escala:** superpone una barra vertical en la imagen que muestra
  qué valor corresponde a cada color
- **Modo escala de grises:** muestra la imagen en blanco y negro en lugar de
  pseudocolor. Útil para análisis cuantitativo o publicaciones científicas.

**Sección AJUSTES DE IMAGEN:**
- **Gamma:** controla el brillo de las señales débiles. Baja el gamma (por ejemplo 0.5)
  si los detalles en zonas oscuras son importantes.
- **Percentil bajo / alto:** definen el rango de contraste. Si pones 0 y 100 usará
  el mínimo y máximo absolutos. Con 2 y 98 ignora el 2 % de píxeles más extremos
  en cada extremo, que suele dar un contraste más útil.
- **Umbral de señal:** valores por debajo de este número se pintan de negro.
  Útil para suprimir el ruido de fondo en bioimagen.

**Sección EXPORTAR:**
- **Exportar banda actual:** guarda la imagen visible ahora mismo como PNG, TIFF o BMP.
- **Exportar todas las bandas:** guarda todas las bandas del cubo como imágenes
  individuales en una carpeta. Útil para crear animaciones o análisis posteriores.
- **Limpiar puntos del gráfico:** elimina todos los píxeles seleccionados.

**Sección INFO DE BANDA:**
Muestra el número de banda, su longitud de onda, y los valores mínimo y máximo
de esa banda específica.

---

## Barra de estado inferior

Muestra en tiempo real las coordenadas del ratón, la banda actual, la longitud
de onda y el valor numérico del píxel bajo el cursor. Cuando el programa está
cargando o exportando, muestra el progreso en porcentaje.

---

## Uso desde código (sin interfaz gráfica)

Si quieres usar las clases directamente en otro programa:

```csharp
using SpecimenFX17.Imaging;

// Cargar el cubo
var cube = HyperspectralCube.Load(@"C:\datos\muestra.hdr");

// Información básica
Console.WriteLine($"Dimensiones: {cube.Samples} × {cube.Lines} px");
Console.WriteLine($"Bandas: {cube.Bands}");
Console.WriteLine($"Rango λ: {cube.Header.Wavelengths[0]:F1} – {cube.Header.Wavelengths[^1]:F1} nm");

// Encontrar la banda más cercana a 650 nm
int banda650 = cube.Header.Wavelengths
    .Select((wl, i) => (diff: Math.Abs(wl - 650.0), i))
    .OrderBy(x => x.diff)
    .First().i;

// Obtener el espectro completo de un píxel
float[] espectro = cube.GetSpectrum(line: 240, sample: 320);

// Renderizar esa banda como imagen BLI
var opciones = new BliRenderOptions
{
    Colormap       = BliColormap.RedFluorescent,
    Gamma          = 0.7f,
    LowPercentile  = 2f,
    HighPercentile = 98f,
    DrawColorbar   = true,
    Wavelength     = cube.Header.Wavelengths[banda650]
};

using var imagen = BliRenderer.RenderBand(cube, banda650, opciones);
imagen.Save(@"C:\salida\bli_650nm.png", System.Drawing.Imaging.ImageFormat.Png);

// Crear imagen RGB combinando tres bandas
int bandaR = /* banda cercana a 650 nm */ banda650;
int bandaG = /* banda cercana a 550 nm */ 0;  // calcular igual
int bandaB = /* banda cercana a 450 nm */ 0;
using var rgb = BliRenderer.RenderRGB(cube, bandaR, bandaG, bandaB, opciones);
rgb.Save(@"C:\salida\rgb_compuesto.png", System.Drawing.Imaging.ImageFormat.Png);
```

---

## Preguntas frecuentes

**¿Qué hago si el programa no encuentra el archivo .raw?**
El `.hdr` y el `.raw` deben estar en la misma carpeta y tener exactamente el
mismo nombre (solo cambia la extensión). Por ejemplo: `muestra.hdr` y `muestra.raw`.

**¿Por qué la imagen sale toda negra o toda blanca?**
Prueba a ajustar los percentiles. Si la señal está concentrada en un rango muy
estrecho, bajar el percentil alto (por ejemplo a 80 %) puede ayudar.

**¿El programa modifica mis archivos originales?**
No. El programa solo lee los archivos `.hdr` y `.raw`. Nunca los modifica.

**¿Puedo abrir imágenes con extensión .img o .dat en lugar de .raw?**
Sí. El programa busca automáticamente extensiones alternativas (.img, .dat,
.bil, .bip, .bsq) si no encuentra el `.raw`.

**¿Qué significa el valor numérico que aparece al pasar el ratón?**
Es el valor original del archivo `.raw` para ese píxel en la banda actual.
Puede ser reflectancia, radiancia, cuentas de detector, o cualquier magnitud
según cómo haya sido calibrado el equipo specimenFX17.
