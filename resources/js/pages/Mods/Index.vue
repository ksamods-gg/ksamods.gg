<script setup lang="ts">
import { Head, router, usePage } from '@inertiajs/vue3';
import { computed, onMounted, reactive, ref, useTemplateRef, watch } from 'vue';
import ModRow from '@/components/ModRow.vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { tagName } from '@/lib/format';
import { rememberedGameVersion, rememberGameVersion } from '@/lib/gameVersion';
import { mods as modsFeed } from '@/routes/feeds';
import type { GameVersion, ModSummary } from '@/types';

const props = defineProps<{
    mods: ModSummary[];
    tagNames: Record<string, string>;
    gameVersions: GameVersion[];
}>();

type Sort = 'relevance' | 'downloads' | 'rating' | 'updated' | 'name';

const SORTS: Sort[] = ['relevance', 'downloads', 'rating', 'updated', 'name'];
const PAGE_SIZES = [10, 20, 50];

// The list's state lives in the query string, so a filtered list can be shared and survives Back.
const params = new URLSearchParams(usePage().url.split('?')[1] ?? '');
const listParam = (name: string) =>
    params.get(name)?.split(',').filter(Boolean) ?? [];
const numberParam = (name: string) => {
    const value = Number(params.get(name));

    return Number.isInteger(value) && value > 0 ? value : null;
};

const search = ref(params.get('q') ?? '');
const sort = ref<Sort>(
    SORTS.find((value) => value === params.get('sort')) ?? 'relevance',
);
const pageSize = ref(
    PAGE_SIZES.find((value) => value === numberParam('per')) ?? 20,
);
const page = ref(numberParam('page') ?? 1);

const filters = reactive({
    // The build the player runs: mods that need a newer one are hidden.
    gameRevision:
        props.gameVersions.find(
            (version) => version.label === params.get('game'),
        )?.revision ?? null,
    loaders: listParam('loader'),
    categories: listParam('tag'),
    types: listParam('type'),
});
const showAllVersions = ref(false);
// On phones the filters fold away behind one button.
const filtersOpen = ref(false);

const NO_LOADER = 'none';

// Every option counts the mods it matches across the whole index.
const count = (match: (mod: ModSummary) => boolean) =>
    props.mods.filter(match).length;

const categories = computed(() =>
    [...new Set(props.mods.flatMap((mod) => mod.tags))]
        .map((tag) => ({
            value: tag,
            label: tagName(tag, props.tagNames),
            count: count((mod) => mod.tags.includes(tag)),
        }))
        .sort((a, b) => a.label.localeCompare(b.label)),
);

const loaders = computed(() => [
    ...[...new Set(props.mods.flatMap((mod) => mod.loader ?? []))].map(
        (loader) => ({
            value: loader,
            label: loader,
            count: count((mod) => mod.loader === loader),
        }),
    ),
    {
        value: NO_LOADER,
        label: 'No loader',
        count: count((mod) => mod.loader === null),
    },
]);

const types = computed(() => [
    { value: 'mod', label: 'Mod', count: count((mod) => mod.type === 'mod') },
    {
        value: 'mod-loader',
        label: 'Mod loader',
        count: count((mod) => mod.type === 'mod-loader'),
    },
]);

const shownVersions = computed(() =>
    showAllVersions.value ? props.gameVersions : props.gameVersions.slice(0, 8),
);

const activeFilterCount = computed(
    () =>
        (filters.gameRevision === null ? 0 : 1) +
        filters.loaders.length +
        filters.categories.length +
        filters.types.length,
);

const query = computed(() => search.value.trim().toLowerCase());

// How well a mod matches the search: its name counts most, then authors, then the summary.
function relevance(mod: ModSummary): number {
    const q = query.value;
    const name = mod.name.toLowerCase();

    return (
        (name.startsWith(q) ? 4 : name.includes(q) ? 3 : 0) +
        (mod.authors.some((a) => a.toLowerCase().includes(q)) ? 2 : 0) +
        ((mod.abstract ?? '').toLowerCase().includes(q) ? 1 : 0)
    );
}

const results = computed(() => {
    const q = query.value;

    return props.mods
        .filter(
            (mod) =>
                (filters.gameRevision === null ||
                    mod.game_min_revision === null ||
                    mod.game_min_revision <= filters.gameRevision) &&
                (filters.loaders.length === 0 ||
                    filters.loaders.includes(mod.loader ?? NO_LOADER)) &&
                filters.categories.every((tag) => mod.tags.includes(tag)) &&
                (filters.types.length === 0 ||
                    filters.types.includes(mod.type)) &&
                (!q || relevance(mod) > 0),
        )
        .sort((a, b) => {
            switch (sort.value) {
                case 'name':
                    return a.name.localeCompare(b.name);
                case 'updated':
                    return b.updated_at.localeCompare(a.updated_at);
                case 'rating':
                    return (
                        (b.rating?.average ?? 0) - (a.rating?.average ?? 0) ||
                        (b.rating?.count ?? 0) - (a.rating?.count ?? 0) ||
                        b.downloads - a.downloads
                    );
                case 'relevance':
                    if (q) {
                        return (
                            relevance(b) - relevance(a) ||
                            b.downloads - a.downloads
                        );
                    }

                    return b.downloads - a.downloads;
                default:
                    return b.downloads - a.downloads;
            }
        });
});

const pageCount = computed(() =>
    Math.max(1, Math.ceil(results.value.length / pageSize.value)),
);
const pageItems = computed(() =>
    results.value.slice(
        (page.value - 1) * pageSize.value,
        page.value * pageSize.value,
    ),
);

// A shared link can name a page the list no longer has.
page.value = Math.min(page.value, pageCount.value);

// Page links: the first, the last, and the pages around the current one.
const pageLinks = computed(() => {
    const links: (number | 'gap')[] = [];

    for (let n = 1; n <= pageCount.value; n++) {
        if (n === 1 || n === pageCount.value || Math.abs(n - page.value) <= 1) {
            links.push(n);
        } else if (links.at(-1) !== 'gap') {
            links.push('gap');
        }
    }

    return links;
});

// A new search, filter or page size starts again at page one.
watch([query, sort, pageSize, filters], () => (page.value = 1), {
    deep: true,
});

// Defaults stay out of the URL, so the plain list keeps its plain address.
const listUrl = computed(() => {
    const game = props.gameVersions.find(
        (version) => version.revision === filters.gameRevision,
    );
    const next = new URLSearchParams(
        Object.entries({
            q: search.value.trim(),
            sort: sort.value === 'relevance' ? '' : sort.value,
            game: game?.label ?? '',
            loader: filters.loaders.join(','),
            tag: filters.categories.join(','),
            type: filters.types.join(','),
            per: pageSize.value === 20 ? '' : String(pageSize.value),
            page: page.value === 1 ? '' : String(page.value),
        }).filter(([, value]) => value !== ''),
    ).toString();

    return next ? `/mods?${next}` : '/mods';
});

// The game version is remembered, so the next visit and each mod page know it. Storage exists
// only in the browser, so it is read after mounting, where the server render cannot differ.
onMounted(() => {
    if (!params.has('game')) {
        const remembered = props.gameVersions.find(
            (version) => version.label === rememberedGameVersion(),
        );

        if (remembered) {
            filters.gameRevision = remembered.revision;
        }
    }
});
watch(
    () => filters.gameRevision,
    (revision) =>
        rememberGameVersion(
            props.gameVersions.find((version) => version.revision === revision)
                ?.label ?? null,
        ),
);

// Client-side, so typing a search never asks the server for anything.
watch(listUrl, (url) =>
    router.replace({ url, preserveState: true, preserveScroll: true }),
);

const resultsTop = useTemplateRef<HTMLElement>('resultsTop');

function goToPage(n: number) {
    page.value = Math.min(Math.max(n, 1), pageCount.value);
    resultsTop.value?.scrollIntoView({ block: 'start' });
}

function clearFilters() {
    filters.gameRevision = null;
    filters.loaders = [];
    filters.categories = [];
    filters.types = [];
    search.value = '';
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const panel =
    'rounded-lg bg-white shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const control =
    'rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
const option =
    'flex cursor-pointer items-center gap-2.5 rounded-md px-2 py-1.5 text-sm hover:bg-[#f4f3ef] dark:hover:bg-[#1c1c1a]';
const pageButton =
    'flex size-8 items-center justify-center rounded-full text-sm tabular-nums hover:bg-[#f4f3ef] disabled:opacity-40 aria-[current=page]:bg-[#1b1b18] aria-[current=page]:font-semibold aria-[current=page]:text-white dark:hover:bg-[#232321] dark:aria-[current=page]:bg-[#EDEDEC] dark:aria-[current=page]:text-[#0a0a0a]';
</script>

<template>
    <PageMeta
        title="Mods"
        description="Search and filter every Kitten Space Agency mod by game version, mod loader and category."
    />
    <Head>
        <link
            rel="alternate"
            type="application/atom+xml"
            title="New mod releases"
            :href="modsFeed('atom').url"
        />
    </Head>

    <AppLayout>
        <div class="mb-6">
            <h1 class="text-2xl font-semibold tracking-tight">
                Mods for Kitten Space Agency
            </h1>
            <p :class="['mt-1 text-sm', muted]">
                {{ mods.length }} listings from the community content index.
                <a
                    :href="modsFeed('atom').url"
                    class="underline underline-offset-4"
                    >New releases feed</a
                >
            </p>
        </div>

        <div class="grid items-start gap-6 lg:grid-cols-[16rem_minmax(0,1fr)]">
            <!-- Filters: a sidebar on wide screens, folded behind a button on phones. -->
            <button
                type="button"
                :class="[
                    panel,
                    'flex items-center justify-between px-4 py-3 text-sm font-medium lg:hidden',
                ]"
                :aria-expanded="filtersOpen"
                aria-controls="mod-filters"
                @click="filtersOpen = !filtersOpen"
            >
                <span
                    >Filters<template v-if="activeFilterCount">
                        ({{ activeFilterCount }})</template
                    ></span
                >
                <svg
                    viewBox="0 0 16 16"
                    fill="currentColor"
                    aria-hidden="true"
                    :class="[
                        'size-4 transition-transform',
                        filtersOpen && 'rotate-180',
                    ]"
                >
                    <path
                        d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z"
                    />
                </svg>
            </button>

            <aside
                id="mod-filters"
                aria-label="Filters"
                :class="[
                    'flex-col gap-3 lg:sticky lg:top-6 lg:flex',
                    filtersOpen ? 'flex' : 'hidden',
                ]"
            >
                <details
                    v-for="group in [
                        { id: 'version', title: 'Game version' },
                        { id: 'loader', title: 'Loader' },
                        { id: 'category', title: 'Category' },
                        { id: 'type', title: 'Type' },
                    ]"
                    :key="group.id"
                    :class="[panel, 'group p-3']"
                >
                    <summary
                        class="flex cursor-pointer list-none items-center justify-between rounded-md px-2 py-1 font-semibold [&::-webkit-details-marker]:hidden"
                    >
                        {{ group.title }}
                        <svg
                            viewBox="0 0 16 16"
                            fill="currentColor"
                            aria-hidden="true"
                            class="size-4 transition-transform group-open:rotate-180"
                        >
                            <path
                                d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z"
                            />
                        </svg>
                    </summary>

                    <fieldset class="mt-2">
                        <legend class="sr-only">{{ group.title }}</legend>

                        <template v-if="group.id === 'version'">
                            <p :class="['mb-1 px-2 text-xs', muted]">
                                Hides mods that need a newer build than yours.
                            </p>
                            <div
                                :class="
                                    showAllVersions &&
                                    'max-h-64 overflow-y-auto'
                                "
                            >
                                <label :class="option">
                                    <input
                                        v-model="filters.gameRevision"
                                        type="radio"
                                        name="game-version"
                                        :value="null"
                                    />
                                    Any version
                                </label>
                                <label
                                    v-for="version in shownVersions"
                                    :key="version.label"
                                    :class="[option, 'font-mono tabular-nums']"
                                >
                                    <input
                                        v-model="filters.gameRevision"
                                        type="radio"
                                        name="game-version"
                                        :value="version.revision"
                                    />
                                    {{ version.label }}
                                </label>
                            </div>
                            <label :class="[option, muted]">
                                <input
                                    v-model="showAllVersions"
                                    type="checkbox"
                                />
                                Show all versions
                            </label>
                        </template>

                        <template v-else>
                            <label
                                v-for="item in group.id === 'loader'
                                    ? loaders
                                    : group.id === 'category'
                                      ? categories
                                      : types"
                                :key="item.value"
                                :class="option"
                            >
                                <input
                                    v-if="group.id === 'loader'"
                                    v-model="filters.loaders"
                                    type="checkbox"
                                    :value="item.value"
                                />
                                <input
                                    v-else-if="group.id === 'category'"
                                    v-model="filters.categories"
                                    type="checkbox"
                                    :value="item.value"
                                />
                                <input
                                    v-else
                                    v-model="filters.types"
                                    type="checkbox"
                                    :value="item.value"
                                />
                                <span class="flex-1">{{ item.label }}</span>
                                <span
                                    :class="['text-xs tabular-nums', muted]"
                                    >{{ item.count }}</span
                                >
                            </label>
                        </template>
                    </fieldset>
                </details>

                <button
                    v-if="activeFilterCount"
                    type="button"
                    :class="['self-start px-2 text-sm hover:underline', muted]"
                    @click="clearFilters"
                >
                    Clear filters
                </button>
            </aside>

            <!-- Results: search, sort and pages over a list of rows. -->
            <section ref="resultsTop" class="min-w-0 scroll-mt-6">
                <label class="block">
                    <span class="sr-only">Search mods</span>
                    <input
                        v-model="search"
                        type="search"
                        placeholder="Search mods by name, description or author"
                        :class="[control, 'w-full py-2.5']"
                    />
                </label>

                <div class="mt-3 flex flex-wrap items-center gap-3">
                    <label class="flex items-center gap-2 text-sm">
                        <span :class="muted">Sort by</span>
                        <select v-model="sort" :class="[control, 'py-1.5']">
                            <option value="relevance">Relevance</option>
                            <option value="downloads">Most downloaded</option>
                            <option value="rating">Highest rated</option>
                            <option value="updated">Recently updated</option>
                            <option value="name">Name</option>
                        </select>
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <span :class="muted">View</span>
                        <select
                            v-model.number="pageSize"
                            :class="[control, 'py-1.5']"
                        >
                            <option :value="10">10</option>
                            <option :value="20">20</option>
                            <option :value="50">50</option>
                        </select>
                    </label>
                    <p :class="['text-sm', muted]" aria-live="polite">
                        {{ results.length }}
                        {{ results.length === 1 ? 'mod' : 'mods' }}
                    </p>

                    <nav
                        v-if="pageCount > 1"
                        aria-label="Pages"
                        class="ml-auto flex items-center gap-1"
                    >
                        <button
                            v-for="(link, i) in pageLinks"
                            :key="i"
                            type="button"
                            :class="pageButton"
                            :disabled="link === 'gap'"
                            :aria-current="link === page ? 'page' : undefined"
                            @click="link !== 'gap' && goToPage(link)"
                        >
                            {{ link === 'gap' ? '…' : link }}
                        </button>
                    </nav>
                </div>

                <p
                    v-if="results.length === 0"
                    :class="[
                        'mt-4 rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                        muted,
                    ]"
                >
                    No mods match.
                    <button
                        type="button"
                        class="underline underline-offset-4"
                        @click="clearFilters"
                    >
                        Clear search and filters
                    </button>
                </p>

                <ul v-else class="mt-4 flex flex-col gap-3">
                    <li v-for="mod in pageItems" :key="mod.id">
                        <ModRow :mod="mod" :tag-names="tagNames" />
                    </li>
                </ul>

                <nav
                    v-if="pageCount > 1"
                    aria-label="Pages, bottom"
                    class="mt-6 flex justify-end gap-1"
                >
                    <button
                        v-for="(link, i) in pageLinks"
                        :key="i"
                        type="button"
                        :class="pageButton"
                        :disabled="link === 'gap'"
                        :aria-current="link === page ? 'page' : undefined"
                        @click="link !== 'gap' && goToPage(link)"
                    >
                        {{ link === 'gap' ? '…' : link }}
                    </button>
                </nav>
            </section>
        </div>
    </AppLayout>
</template>
