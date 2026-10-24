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

# CVE-2026-109618 and CVE-2026-109619 (PHP RCE, advisory of 21 Oct 2026) are fixed in 8.4.27.
# Uploads of vehicles and saves depend on that fix, so a build on an older base image fails.
RUN php -r 'exit(version_compare(PHP_VERSION, "8.4.27", "<") ? 1 : 0);' \
    || (echo "The base image runs PHP $(php -r 'echo PHP_VERSION;'), older than 8.4.27. Rebuild without cache." && exit 1)

# Vehicles and saves arrive as uploads: the largest is a save of up to 100 MB (SHARE_MAX_SAVE_BYTES).
ENV PHP_POST_MAX_SIZE=110M \
    PHP_UPLOAD_MAX_FILE_SIZE=105M \
    PHP_FILE_UPLOADS=On \
    NGINX_CLIENT_MAX_BODY_SIZE=110M

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
