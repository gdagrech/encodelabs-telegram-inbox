FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime

# nginx serves the compiled bundle and proxies /api and /hubs to the API container, so the browser
# only ever talks to one origin and no CORS configuration is needed.
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/encodelabs-inbox-frontend/browser /usr/share/nginx/html

EXPOSE 80
