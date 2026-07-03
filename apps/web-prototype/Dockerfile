# Prototipo visual ECOREX.tareas (TanStack Start + Vite). Solo referencia de diseño.
# Servidor de desarrollo Vite para comparaciones visuales (no es build de produccion).
FROM oven/bun:1

WORKDIR /app

# Dependencias (capa cacheable)
COPY package.json bun.lockb bunfig.toml ./
RUN bun install --frozen-lockfile

# Codigo fuente
COPY . .

EXPOSE 8080

# --host 0.0.0.0 para que sea accesible fuera del contenedor.
CMD ["bun", "run", "dev", "--", "--host", "0.0.0.0", "--port", "8080"]
