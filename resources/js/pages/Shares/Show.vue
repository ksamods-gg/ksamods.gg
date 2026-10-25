<script setup lang="ts">
import { Link, router, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatBytes, formatDate } from '@/lib/format';
import { show as modPage } from '@/routes/mods';
import { create, index, takeDown } from '@/routes/shares';
import type { ShareDetail } from '@/types';

const props = defineProps<{
    share: ShareDetail;
    owns: boolean;
    canTakeDown: boolean;
}>();

const page = usePage();
const listing = props.share.type === 'vehicle' ? 'vehicles' : 'saves';

// What search engines read: a player's creation for the game, and where it sits on the site.
const jsonLd = computed(() => {
    const url = `${page.props.appUrl}/${listing}/${encodeURIComponent(props.share.id)}`;

    return {
        '@graph': [
            {
                '@type': 'CreativeWork',
                '@id': url,
                name: props.share.name,
                description: props.share.abstract,
                url,
                genre:
                    props.share.type === 'vehicle'
                        ? 'Kitten Space Agency vehicle'
                        : 'Kitten Space Agency save game',
                author: props.share.authors.map((name) => ({
                    '@type': 'Person',
                    name,
                })),
                license: props.share.license,
                keywords: props.share.tags.join(', ') || undefined,
                version: props.share.version ?? undefined,
                dateModified: props.share.updated_at,
            },
            {
                '@type': 'BreadcrumbList',
                itemListElement: [
                    {
                        '@type': 'ListItem',
                        position: 1,
                        name: listing === 'vehicles' ? 'Vehicles' : 'Saves',
                        item: `${page.props.appUrl}/${listing}`,
                    },
                    {
                        '@type': 'ListItem',
                        position: 2,
                        name: props.share.name,
                    },
                ],
            },
        ],
    };
});

const kinds: Record<string, string> = {
    required: 'Required',
    recommends: 'Recommended',
    optional: 'Optional',
    suggests: 'Suggested',
    conflict: 'Conflicts with',
};

function takeDownRelease(id: number, version: string) {
    if (
        confirm(
            `Take down ${props.share.name} ${version}? Its archive is deleted and its download answers that it is gone. This cannot be undone.`,
        )
    ) {
        router.post(takeDown(id).url, {}, { preserveScroll: true });
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <PageMeta
        :title="`${share.name} - KSA ${share.type}`"
        :description="share.abstract"
        :json-ld="jsonLd"
    />

    <AppLayout>
        <Link
            :href="index(listing)"
            :class="['text-sm hover:underline', muted]"
        >
            ← {{ listing === 'vehicles' ? 'Vehicles' : 'Saves' }}
        </Link>

        <p
            v-if="page.flash.status"
            role="status"
            class="mt-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ page.flash.status }}
        </p>

        <div class="mt-3 grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div class="flex min-w-0 flex-col gap-4">
                <div>
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ share.name }}
                    </h1>
                    <p :class="['text-sm', muted]">
                        {{ share.type === 'vehicle' ? 'Vehicle' : 'Save' }}
                        by {{ share.authors.join(', ') }}
                    </p>
                    <p class="mt-3">{{ share.abstract }}</p>
                </div>

                <section v-if="share.description" :class="card">
                    <h2 class="sr-only">Description</h2>
                    <p class="text-sm break-words whitespace-pre-line">
                        {{ share.description }}
                    </p>
                </section>

                <section :class="card">
                    <h2 class="mb-3 font-semibold">Mods it needs</h2>
                    <ul
                        v-if="share.dependencies.length"
                        class="divide-y divide-[#19140014] text-sm dark:divide-[#3E3E3A]/60"
                    >
                        <li
                            v-for="dependency in share.dependencies"
                            :key="`${dependency.kind}-${dependency.id}`"
                            class="flex flex-wrap items-baseline justify-between gap-x-3 py-2"
                        >
                            <span>
                                <Link
                                    v-if="dependency.name && dependency.id"
                                    :href="modPage(dependency.id)"
                                    class="font-medium hover:underline"
                                    >{{ dependency.name }}</Link
                                >
                                <template v-else>{{ dependency.id }}</template>
                                <span :class="['ml-2 text-xs', muted]">{{
                                    kinds[dependency.kind] ?? dependency.kind
                                }}</span>
                            </span>
                            <span
                                v-if="dependency.min"
                                :class="['text-xs', muted]"
                                >{{ dependency.min }} or newer</span
                            >
                        </li>
                    </ul>
                    <p v-else :class="['text-sm', muted]">
                        Nothing besides the game.
                    </p>
                </section>

                <section :class="card">
                    <h2 class="mb-3 font-semibold">Releases</h2>
                    <ul
                        class="divide-y divide-[#19140014] text-sm dark:divide-[#3E3E3A]/60"
                    >
                        <li
                            v-for="release in share.releases"
                            :key="release.id"
                            class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2.5"
                        >
                            <span class="flex flex-wrap items-baseline gap-x-3">
                                <span class="font-mono font-medium"
                                    >v{{ release.version }}</span
                                >
                                <span
                                    v-if="release.status !== 'stable'"
                                    class="rounded bg-accent-wash px-1.5 py-0.5 text-xs text-accent"
                                    >{{ release.status }}</span
                                >
                                <span :class="['text-xs', muted]"
                                    >{{ formatDate(release.date) }} · game
                                    {{ release.game_version }} ·
                                    {{ formatBytes(release.size) }}</span
                                >
                            </span>
                            <span class="flex items-center gap-3 text-xs">
                                <span v-if="release.taken_down" :class="muted"
                                    >Taken down</span
                                >
                                <a
                                    v-else-if="release.url"
                                    :href="release.url"
                                    class="underline underline-offset-4"
                                    >Download</a
                                >
                                <button
                                    v-if="canTakeDown && !release.taken_down"
                                    type="button"
                                    class="text-[#f53003] hover:underline dark:text-[#FF4433]"
                                    @click="
                                        takeDownRelease(
                                            release.id,
                                            release.version,
                                        )
                                    "
                                >
                                    Take down
                                </button>
                            </span>
                        </li>
                    </ul>
                </section>
            </div>

            <aside class="flex flex-col gap-4 lg:sticky lg:top-6 lg:self-start">
                <div :class="[card, 'flex flex-col gap-3 text-sm']">
                    <p :class="muted">
                        Install it from your mod manager, such as Borea, which
                        installs the mods it needs first. A manual download is
                        the folder to unzip into your
                        {{ share.type === 'vehicle' ? 'Vehicles' : 'saves' }}
                        folder.
                    </p>
                    <Link
                        v-if="owns"
                        :href="create({ query: { listing: share.id } })"
                        class="rounded-md bg-[#1b1b18] px-4 py-2 text-center font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                        >Upload a new version</Link
                    >
                </div>
                <dl
                    :class="[
                        card,
                        'grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm',
                    ]"
                >
                    <dt :class="muted">Id</dt>
                    <dd class="truncate font-mono">{{ share.id }}</dd>
                    <dt :class="muted">License</dt>
                    <dd>{{ share.license }}</dd>
                    <template v-if="share.tags.length">
                        <dt :class="muted">Tags</dt>
                        <dd>{{ share.tags.join(', ') }}</dd>
                    </template>
                    <template v-if="share.pull_request_url">
                        <dt :class="muted">Index</dt>
                        <dd>
                            <a
                                :href="share.pull_request_url"
                                target="_blank"
                                rel="noopener noreferrer"
                                class="underline underline-offset-4"
                                >Listing pull request</a
                            >
                        </dd>
                    </template>
                </dl>
            </aside>
        </div>
    </AppLayout>
</template>
