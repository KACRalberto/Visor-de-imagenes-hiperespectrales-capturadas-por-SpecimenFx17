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
        df = pd.read_csv(args.input_csv, sep=None, engine='python') # Detecta el separador (, o ;) automáticamente
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
    
    # Separar la X (Solo las bandas)
    X = df.filter(regex='^Wl_')

    print("🛡️ Limpiando formato de Excel en las bandas...")
    # BLINDAJE CONTRA EXCEL: Reemplazar comas por puntos y forzar TODO a números
    for col in X.columns:
        # Pasamos a texto, cambiamos comas por puntos
        X[col] = X[col].astype(str).str.replace(',', '.')
        # Forzamos conversión numérica. Lo que sea texto roto (ej: 30.952.382) se volverá NaN
        X[col] = pd.to_numeric(X[col], errors='coerce')

    # Reemplazar infinitos y los nuevos NaN por 0
    X = X.replace([np.inf, -np.inf], np.nan)
    X = X.fillna(0)

    print(f"🧠 Entrenando modelo PLS con {args.components} componentes sobre {X.shape[1]} bandas reales...")
    pls = PLSRegression(n_components=args.components)
    pls.fit(X, y)

    intercept = pls.intercept_[0]
    coefs = pls.coef_.flatten()

    print(f"💾 Guardando modelo en: {args.output}")
    try:
        with open(args.output, 'w') as f:
            f.write(f"Intercept,{intercept}\n")
            coef_str = ",".join(map(str, coefs))
            f.write(f"Coefs,{coef_str}\n")
        print("✅ ¡Modelo exportado con éxito!")
    except Exception as e:
        print(f"❌ Error al guardar el archivo: {e}")

if __name__ == "__main__":
    main()
