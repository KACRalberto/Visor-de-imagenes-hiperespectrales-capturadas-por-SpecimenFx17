import argparse
import sys
import pandas as pd
from sklearn.cross_decomposition import PLSRegression

##python entrenar_pls.py mis_naranjas.csv --target GradosBrix --components 5 --output modelo_naranjas_v1.csv

def main():
    # 1. Configurar los argumentos de la línea de comandos
    parser = argparse.ArgumentParser(description="Entrena un modelo PLS y lo exporta para SpecimenFX17Viewer.")
    parser.add_argument("input_csv", help="Ruta al archivo CSV con los espectros y la variable objetivo.")
    parser.add_argument("--target", required=True, help="Nombre de la columna a predecir (ej. Brix).")
    parser.add_argument("--components", type=int, default=10, help="Número de componentes PLS (defecto: 10).")
    parser.add_argument("--output", default="modelo_pls.csv", help="Ruta del archivo CSV de salida (defecto: modelo_pls.csv).")

    args = parser.parse_args()

    # 2. Cargar y validar los datos
    try:
        print(f"📥 Cargando datos desde: {args.input_csv}")
        df = pd.read_csv(args.input_csv)
    except FileNotFoundError:
        print(f"❌ Error: No se encontró el archivo '{args.input_csv}'")
        sys.exit(1)

    if args.target not in df.columns:
        print(f"❌ Error: La columna objetivo '{args.target}' no existe en el CSV.")
        print(f"Columnas disponibles: {', '.join(df.columns)}")
        sys.exit(1)

    # Filtrar solo las columnas que empiezan por 'Wl_' (Longitudes de onda que exporta C#)
    X = df.filter(regex='^Wl_')
    
    if X.empty:
        print("❌ Error: No se encontraron columnas de espectro (deben empezar por 'Wl_').")
        sys.exit(1)
        
    y = df[args.target]

    # 3. Entrenar el modelo PLS
    print(f"🧠 Entrenando modelo PLS con {args.components} componentes sobre {len(X.columns)} bandas...")
    pls = PLSRegression(n_components=args.components)
    pls.fit(X, y)

    # 4. Extraer coeficientes en la escala original
    coefs = pls.coef_.flatten()
    
    # Scikit-learn devuelve el intercepto como un array si es 2D, o un escalar. Lo normalizamos:
    intercept = pls.intercept_[0] if hasattr(pls.intercept_, '__iter__') else pls.intercept_

    # 5. Exportar a CSV para C#
    print(f"💾 Exportando coeficientes a: {args.output}")
    with open(args.output, "w", encoding="utf-8") as f:
        f.write(f"Intercept,{intercept}\n")
        f.write("Coefs," + ",".join(map(str, coefs)) + "\n")

    print(f"✅ ¡Proceso completado con éxito! El modelo tiene un R² de {pls.score(X, y):.3f} sobre los datos de entrenamiento.")

if __name__ == "__main__":
    main()
