import pandas as pd
import numpy as np
import argparse
from sklearn.cross_decomposition import PLSRegression

def main():
    parser = argparse.ArgumentParser(description="Entrenador de modelos PLS para SpecimenFX17")
    parser.add_argument("input_csv", help="Ruta al archivo CSV con los espectros")
    parser.add_argument("--target", required=True, help="Nombre de la columna a predecir (ej. Brix)")
    parser.add_argument("--components", type=int, default=10, help="Número de componentes principales PLS")
    parser.add_argument("--output", default="modelo_entrenado.csv", help="Nombre del archivo de salida")
    args = parser.parse_args()

    print(f"📥 Cargando datos desde: {args.input_csv}")
    
    try:
        df = pd.read_csv(args.input_csv, sep=None, engine='python')
    except Exception as e:
        print(f"❌ Error al leer el archivo CSV: {e}")
        return

    if args.target not in df.columns:
        print(f"❌ Error: No se encontró la columna '{args.target}'. Revisa cómo la llamaste en Excel.")
        return
        
    df = df.dropna(subset=[args.target])
    print(f"🧹 Filas válidas tras limpiar datos sin {args.target}: {len(df)}")

    # Separar la Y
    y = df[args.target]
    
    # Separar la X (Solo las bandas). Añadimos .copy() para evitar el SettingWithCopyWarning
    X = df.filter(regex='^Wl_').copy()

    print("🛡️ Limpiando formato de Excel en las bandas...")
    for col in X.columns:
        X[col] = X[col].astype(str).str.replace(',', '.')
        X[col] = pd.to_numeric(X[col], errors='coerce')

    X = X.replace([np.inf, -np.inf], np.nan)
    X = X.fillna(0)

    # SOLUCIÓN AL CRASHEO: Ajustar dinámicamente los componentes al número de filas
    n_samples = X.shape[0]
    n_features = X.shape[1]
    
    if n_samples == 0:
        print("❌ Error: No hay datos válidos para entrenar.")
        return

    # Scikit-learn no permite más componentes que el número de muestras
    comp_to_use = min(args.components, n_samples)
    if comp_to_use < args.components:
        print(f"⚠️ Aviso automático: Tienes muy pocos datos ({n_samples} filas). Reduciendo componentes PLS de {args.components} a {comp_to_use} para evitar crasheos.")

    print(f"🧠 Entrenando modelo PLS con {comp_to_use} componentes sobre {n_features} bandas reales...")
    pls = PLSRegression(n_components=comp_to_use)
    pls.fit(X, y)

    # Extraer la ecuación matemática
    intercept = pls.intercept_[0] if isinstance(pls.intercept_, (list, np.ndarray)) else pls.intercept_
    coefs = pls.coef_

    # Guardar archivo con el formato exacto que pide el programa en C#
    with open(args.output, 'w') as f:
        f.write(f"Intercepto,{intercept}\n")
        f.write("Coeficientes," + ",".join(map(str, coefs.flatten())) + "\n")

    print(f"✅ ¡Éxito total! Modelo guardado en: {args.output}")
    print("👉 Ahora puedes cargar este archivo en SpecimenFX17 y generar tu mapa de °Brix.")

if __name__ == "__main__":
    main()
