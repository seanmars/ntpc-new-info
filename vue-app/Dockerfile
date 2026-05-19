FROM node:22-alpine AS build
WORKDIR /app

RUN corepack enable

COPY package.json pnpm-lock.yaml ./
RUN --mount=type=cache,id=pnpm,target=/root/.local/share/pnpm/store \
    pnpm install --frozen-lockfile

COPY . .
RUN pnpm run build


FROM nginx:1.27-alpine AS final

ENV API_PROXY_PASS=http://webapi:8080
ENV NGINX_ENVSUBST_FILTER='^(API_PROXY_PASS)$'

COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q --spider http://localhost:8000/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
