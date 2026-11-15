<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { BOREA_RELEASES_URL, boreaPackLink, boreaSupported } from '@/lib/borea';
import { isMobileDevice } from '@/lib/device';
import { formatDate } from '@/lib/format';
import { show as modShow } from '@/routes/mods';
import { index, newVersion } from '@/routes/packs';
import type { PackDetail } from '@/types';

const props = defineProps<{ pack: PackDetail }>();

const page = usePage();
// Borea installs a pack from its own pack page, which this link opens; it runs on Windows and Linux.
const borea =
    !isMobileDevice(page.props.userAgent) &&
    boreaSupported(page.props.userAgent)
        ? boreaPackLink(props.pack.id)
        : null;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const primaryButton =
    'rounded-md bg-[#1b1b18] px-5 py-2.5 text-center text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
</script>

<template>
    <PageMeta :title="pack.name" :description="pack.abstract ?? pack.name" />

    <AppLayout>
        <Link :href="index()" :class="['text-sm hover:underline', muted]"
            >← {{ $t('Mod packs') }}</Link
        >

        <div
            class="mt-3 flex flex-col gap-5 sm:flex-row sm:items-start sm:justify-between"
        >
            <div class="flex min-w-0 gap-4">
                <ModIcon
                    :name="pack.name"
                    :src="pack.icon"
                    class="size-16 shrink-0 rounded-xl text-2xl sm:size-20"
                />
                <div class="min-w-0">
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ pack.name }}
                    </h1>
                    <p v-if="pack.authors.length" :class="['text-sm', muted]">
                        {{
                            $t('by :authors', {
                                authors: pack.authors.join(', '),
                            })
                        }}
                    </p>
                    <p
                        v-if="pack.abstract"
                        class="mt-2 max-w-prose text-[#4a4a46] dark:text-[#c4c3be]"
                    >
                        {{ pack.abstract }}
                    </p>
                </div>
            </div>

            <div class="flex shrink-0 flex-col sm:items-end">
                <a v-if="borea" :href="borea" :class="primaryButton">{{
                    $t('Open in Borea')
                }}</a>
                <p :class="['mt-2 max-w-64 text-xs sm:text-right', muted]">
                    {{
                        $t(
                            'Borea installs the pack’s mods at these versions, with everything they need.',
                        )
                    }}
                    <a
                        :href="BOREA_RELEASES_URL"
                        target="_blank"
                        rel="noopener noreferrer"
                        class="underline underline-offset-4"
                        >{{ $t('Get Borea') }}</a
                    >
                </p>
                <!-- For the pack's owner; the form checks the owner record on GitHub. A plain link: it may go on to GitHub. -->
                <a
                    v-if="page.props.auth.user"
                    :href="newVersion(pack.id).url"
                    class="mt-3 text-sm underline underline-offset-4"
                    >{{ $t('Publish a new version') }}</a
                >
            </div>
        </div>

        <p
            v-if="page.flash.status"
            role="status"
            class="mt-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ page.flash.status }}
        </p>

        <div
            class="mt-8 grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]"
        >
            <div class="flex min-w-0 flex-col gap-6">
                <section :class="card">
                    <h2 class="mb-3 font-semibold">
                        {{
                            pack.mods.length === 1
                                ? $t('1 mod in this pack')
                                : $t(':count mods in this pack', {
                                      count: pack.mods.length,
                                  })
                        }}
                    </h2>
                    <ul
                        class="divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                    >
                        <li
                            v-for="mod in pack.mods"
                            :key="mod.id"
                            class="flex items-start gap-3 py-3 first:pt-0 last:pb-0"
                        >
                            <ModIcon
                                :name="mod.name"
                                :src="mod.icon"
                                class="size-10 shrink-0 rounded-lg"
                            />
                            <div class="min-w-0">
                                <p
                                    class="flex flex-wrap items-baseline gap-x-2"
                                >
                                    <Link
                                        v-if="mod.listed"
                                        :href="modShow(mod.id)"
                                        class="font-medium hover:underline"
                                        >{{ mod.name }}</Link
                                    >
                                    <span v-else class="font-medium">{{
                                        mod.name
                                    }}</span>
                                    <span
                                        class="rounded bg-[#f4f3ef] px-1.5 py-0.5 font-mono text-xs tabular-nums dark:bg-[#232321]"
                                        >{{ mod.version }}</span
                                    >
                                </p>
                                <p
                                    v-if="mod.abstract"
                                    :class="[
                                        'mt-0.5 line-clamp-2 text-sm',
                                        muted,
                                    ]"
                                >
                                    {{ mod.abstract }}
                                </p>
                            </div>
                        </li>
                    </ul>
                </section>

                <!-- Rendered server-side from the index with raw HTML stripped. -->
                <article
                    v-if="pack.description"
                    :class="['markdown', card]"
                    v-html="pack.description"
                />
            </div>

            <aside class="flex flex-col gap-6 text-sm">
                <section :class="card">
                    <h2 class="mb-3 font-semibold">{{ $t('Details') }}</h2>
                    <dl
                        class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2"
                    >
                        <dt :class="muted">{{ $t('Version') }}</dt>
                        <dd class="font-mono tabular-nums">
                            {{ pack.version }}
                        </dd>
                        <template v-if="pack.updated_at">
                            <dt :class="muted">{{ $t('Updated') }}</dt>
                            <dd>{{ formatDate(pack.updated_at) }}</dd>
                        </template>
                        <template v-if="pack.game_min">
                            <dt :class="muted">{{ $t('Game') }}</dt>
                            <dd>
                                {{
                                    $t(':version or newer', {
                                        version: pack.game_min,
                                    })
                                }}
                            </dd>
                        </template>
                        <template v-if="pack.license">
                            <dt :class="muted">{{ $t('License') }}</dt>
                            <dd>{{ pack.license }}</dd>
                        </template>
                        <dt :class="muted">{{ $t('Id') }}</dt>
                        <dd class="truncate font-mono">{{ pack.id }}</dd>
                    </dl>
                </section>

                <section v-if="Object.keys(pack.links).length" :class="card">
                    <h2 class="mb-3 font-semibold">{{ $t('Links') }}</h2>
                    <ul class="flex flex-col gap-1.5">
                        <li v-for="(url, name) in pack.links" :key="name">
                            <a
                                :href="url"
                                target="_blank"
                                rel="noopener noreferrer nofollow"
                                class="capitalize underline underline-offset-4"
                                >{{ name }}</a
                            >
                        </li>
                    </ul>
                </section>

                <section v-if="pack.versions.length > 1" :class="card">
                    <h2 class="mb-3 font-semibold">{{ $t('Versions') }}</h2>
                    <ul class="flex flex-col gap-1.5">
                        <li
                            v-for="version in pack.versions"
                            :key="version.version"
                            class="flex items-baseline justify-between gap-3"
                        >
                            <span class="font-mono tabular-nums">{{
                                version.version
                            }}</span>
                            <span :class="['text-xs', muted]">
                                <template v-if="version.date"
                                    >{{ formatDate(version.date) }} ·
                                </template>
                                {{
                                    version.mods === 1
                                        ? $t('1 mod')
                                        : $t(':count mods', {
                                              count: version.mods,
                                          })
                                }}
                            </span>
                        </li>
                    </ul>
                </section>
            </aside>
        </div>
    </AppLayout>
</template>
