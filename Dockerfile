# Build stage: needs PHP too, because the Wayfinder Vite plugin runs artisan.
FROM serversideup/php:8.4-cli AS build
USER root
COPY --from=node:22-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm

WORKDIR /var/www/html
COPY composer.json composer.lock ./
RUN composer install --no-dev --no-scripts --no-autoloader --prefer-dist --no-interaction
COPY package.json package-lock.json .npmrc ./
RUN npm ci

# Baked into the JS bundle, so they must be known at build time.
ARG VITE_APP_NAME
ARG VITE_REVERB_APP_KEY
ARG VITE_REVERB_HOST
ARG VITE_REVERB_PORT=443
ARG VITE_REVERB_SCHEME=https

COPY . .
RUN composer dump-autoload --optimize --no-dev \
    && npm run build:ssr \
    && rm -rf node_modules

FROM serversideup/php:8.4-fpm-nginx
ENV PHP_OPCACHE_ENABLE=1
# The image's health check (and Coolify's) asks Laravel, so a container is only live once it serves.
ENV HEALTHCHECK_PATH=/up

# Mitigations for CVE-2026-109618 and CVE-2026-109619 (PHP RCE, fixed in 8.4.27), recommended
# by the PHP security advisory of 21 Oct 2026 until the base image ships the fix. The site takes
# no file uploads, so request bodies stay small. No memory_limit: the container's own memory
# limit (set in Coolify) bounds a request instead, since PHP's limit failure is the attack path.
# After moving to 8.4.27, restore the memory limit; the rest can stay.
ENV PHP_MEMORY_LIMIT=-1 \
    PHP_POST_MAX_SIZE=8M \
    PHP_UPLOAD_MAX_FILE_SIZE=2M \
    PHP_FILE_UPLOADS=Off \
    NGINX_CLIENT_MAX_BODY_SIZE=8M

# Inertia's SSR server runs beside nginx and PHP-FPM, so it ships and rolls with every deploy.
# The bundle carries its own dependencies; only the node binary is needed.
COPY --from=node:22-slim /usr/local/bin/node /usr/local/bin/node
USER root
RUN mkdir -p /etc/s6-overlay/s6-rc.d/inertia-ssr/dependencies.d \
    && echo longrun > /etc/s6-overlay/s6-rc.d/inertia-ssr/type \
    && touch /etc/s6-overlay/s6-rc.d/inertia-ssr/dependencies.d/base \
        /etc/s6-overlay/user-bundles.d/user/contents.d/inertia-ssr \
    && printf '#!/command/with-contenv sh\nexec node /var/www/html/bootstrap/ssr/app.js\n' \
        > /etc/s6-overlay/s6-rc.d/inertia-ssr/run \
    && chmod +x /etc/s6-overlay/s6-rc.d/inertia-ssr/run \
    && echo 'session.upload_progress.enabled = 0' > /usr/local/etc/php/conf.d/zz-ksamods-security.ini
USER www-data

COPY --from=build --chown=www-data:www-data /var/www/html /var/www/html
