import pandas as pd
import numpy as np

# 1. Cargar tus datos reales exportados
archivo_entrada = 'espectros_naranjas.csv.csv'
print(f"Cargando {archivo_entrada}...")
df = pd.read_csv(archivo_entrada, sep=None, engine='python')

# 2. Calcular el centro de la imagen exportada
centro_x = df['PixelX'].mean()
centro_y = df['PixelY'].mean()

# 3. Calcular la distancia de cada píxel al centro
distancia = np.sqrt((df['PixelX'] - centro_x)**2 + (df['PixelY'] - centro_y)**2)
distancia_max = distancia.max() if distancia.max() > 0 else 1

# 4. Generar Brix falsos: 15 en el centro, bajando a 8 en los bordes
# Añadimos un poquito de ruido aleatorio (0.2) para que el modelo tenga que "esforzarse" en aprender
np.random.seed(42) # Para que siempre salga igual
df['Brix'] = 15.0 - ((distancia / distancia_max) * 7.0) + np.random.normal(0, 0.2, len(df))

# 5. Guardar el nuevo CSV simulado
archivo_salida = 'datos_simulados.csv'
df.to_csv(archivo_salida, index=False)
print(f"¡Éxito! Se ha generado el archivo '{archivo_salida}' con una columna 'Brix' perfecta para probar.")
