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
    && npm run build \
    && rm -rf node_modules

FROM serversideup/php:8.4-fpm-nginx
ENV PHP_OPCACHE_ENABLE=1
COPY --from=build --chown=www-data:www-data /var/www/html /var/www/html
