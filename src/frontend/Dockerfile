FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# Runs the unit tests with everything inside the image. Building with `--target tests` is how
# scripts/verify.sh runs them, which keeps the result identical on any machine: the Angular CLI
# needs Node >= 24.15, and reading a bind-mounted node_modules from the host is slow enough that
# Vitest's fixed worker-start timeout expires before the suite begins.
FROM build AS tests

# Changing this argument on every run defeats the layer cache for the test step alone, so the suite
# genuinely executes each time while `npm ci` and the build stay cached. Without it a cached layer
# reports success without having run a single test, which is worse than having no gate at all.
ARG TEST_RUN_ID=unspecified
RUN echo "run ${TEST_RUN_ID}" > /dev/null && npm test

FROM nginx:1.29-alpine AS runtime

# nginx serves the compiled bundle and proxies /api and /hubs to the API container, so the browser
# only ever talks to one origin and no CORS configuration is needed.
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/encodelabs-inbox-frontend/browser /usr/share/nginx/html

EXPOSE 80
