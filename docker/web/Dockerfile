# =========
# STAGE 1: Build (Node)
# =========
FROM node:26.13.1-alpine AS build
WORKDIR /app

# Agora copia o resto do código
COPY ./src/Bibliotheca.Frontend/ /app/
RUN npm ci

# --- Variáveis de build (ex.: Vite) ---
# Exemplo: docker build --build-arg VITE_API_URL=https://api.example.com .
#ARG VITE_API_URL
#ENV VITE_API_URL=${VITE_API_URL}

# Exemplo (Vue CLI): --build-arg VUE_APP_API_URL=...
#ARG VUE_APP_API_URL
#ENV VUE_APP_API_URL=${VUE_APP_API_URL}

# Compila (Vite e Vue CLI usam 'build' por convenção)
RUN npm run build

# =========
# STAGE 2: Runtime (Nginx estático)
# =========
FROM nginx:1.30.8-alpine
RUN apk upgrade --no-cache && apk add --no-cache nodejs
COPY docker/web/runtime-config.mjs /opt/bibliotheca/runtime-config.mjs

LABEL pt.labsoft.bibliotheca.image.title="bibliotheca-web"
LABEL pt.labsoft.bibliotheca.image.description="Web Frontend for Bibliotheca App"
LABEL pt.labsoft.bibliotheca.image.version="1.0.0"
LABEL pt.labsoft.bibliotheca.image.authors="Luis Silva"
LABEL pt.labsoft.bibliotheca.image.url="https://github.com/labsilva"
LABEL pt.labsoft.bibliotheca.image.source="https://github.com/labsilva/bibliotheca"
LABEL pt.labsoft.bibliotheca.image.documentation="https://github.com/labsilva/bibliotheca"
#LABEL pt.labsoft.bibliotheca.image.licenses="MIT"
#LABEL pt.labsoft.bibliotheca.image.revision="commit_sha"

ENV NODE_ENV=production

# Remove config default e adiciona a nossa com fallback de SPA
RUN rm -f /etc/nginx/conf.d/default.conf
COPY src/Bibliotheca.Frontend/nginx.conf /etc/nginx/conf.d/app.conf

# Copia os ficheiros gerados (dist) do estágio de build
COPY --from=build /app/dist /usr/share/nginx/html

#COPY src/Bibliotheca.Frontend/docker-entrypoint.sh /entrypoint.sh
COPY src/Bibliotheca.Frontend/docker-entrypoint.sh /docker-entrypoint.d/10-runtime-env.sh
RUN chmod +x /docker-entrypoint.d/10-runtime-env.sh \
    && sed -i 's/\r$//' /docker-entrypoint.d/10-runtime-env.sh

# (Opcional) Saúde do container
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://127.0.0.1/ || exit 1

EXPOSE 80
#ENTRYPOINT ["/entrypoint.sh"]
#CMD ["nginx", "-g", "daemon off;"]