<script setup lang="ts">
import { Head, Link, usePage } from '@inertiajs/vue3';
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue';
import FollowButton from '@/components/FollowButton.vue';
import ModIcon from '@/components/ModIcon.vue';
import CommentsPanel from '@/components/mods/CommentsPanel.vue';
import Gallery from '@/components/mods/Gallery.vue';
import ModRelationGroups from '@/components/mods/ModRelationGroups.vue';
import ReviewsPanel from '@/components/mods/ReviewsPanel.vue';
import PageMeta from '@/components/PageMeta.vue';
import Stars from '@/components/Stars.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import {
    BOREA_RELEASES_URL,
    boreaInstallLink,
    boreaModLink,
    boreaSupported,
} from '@/lib/borea';
import { isMobileDevice } from '@/lib/device';
import { formatBytes, formatDate, formatNumber, tagName } from '@/lib/format';
import { rememberedGameVersion, rememberGameVersion } from '@/lib/gameVersion';
import { cn } from '@/lib/utils';
import { mod as modFeed } from '@/routes/feeds';
import { edit } from '@/routes/listings';
import { index, show } from '@/routes/mods';
import type { Comments, Dependent, GameVersion, Mod, Reviews } from '@/types';

const props = defineProps<{
    mod: Mod;
    tagNames: Record<string, string>;
    reviews: Reviews;
    comments: Comments;
    tab: 'gallery' | 'dependents' | 'releases' | 'reviews' | 'comments' | null;
    // Listings whose newest release needs this one.
    dependents: Dependent[];
    gameVersions: GameVersion[];
    following: boolean;
}>();

const latest = computed(
    () =>
        props.mod.releases.find((release) => release.status === 'stable') ??
        props.mod.releases[0],
);

// What search engines read about the mod: a free application for the game, with its rating and
// where it sits on the site.
const page = usePage();
const jsonLd = computed(() => {
    const url = `${page.props.appUrl}/mods/${encodeURIComponent(props.mod.id)}`;

    return {
        '@graph': [
            {
                '@type': 'SoftwareApplication',
                '@id': url,
                name: props.mod.name,
                description: props.mod.abstract ?? undefined,
                url,
                image: props.mod.icon ?? undefined,
                applicationCategory: 'GameApplication',
                applicationSubCategory: 'Kitten Space Agency mod',
                operatingSystem: 'Windows',
                softwareVersion: latest.value?.version,
                license: props.mod.license ?? undefined,
                author: props.mod.authors.map((name) => ({
                    '@type': 'Person',
                    name,
                })),
                datePublished: props.mod.published_at,
                dateModified: props.mod.updated_at,
                offers: { '@type': 'Offer', price: 0, priceCurrency: 'USD' },
                aggregateRating:
                    props.reviews.count && props.reviews.average
                        ? {
                              '@type': 'AggregateRating',
                              ratingValue: props.reviews.average,
                              ratingCount: props.reviews.count,
                              bestRating: 5,
                              worstRating: 1,
                          }
                        : undefined,
            },
            {
                '@type': 'BreadcrumbList',
                itemListElement: [
                    {
                        '@type': 'ListItem',
                        position: 1,
                        name: 'Mods',
                        item: `${page.props.appUrl}/mods`,
                    },
                    { '@type': 'ListItem', position: 2, name: props.mod.name },
                ],
            },
        ],
    };
});

// Borea only handles its links on Windows and Linux, so elsewhere its buttons would do nothing.
const { userAgent } = page.props;
// Mods install on a desktop, so phones and tablets get a note instead of download buttons.
const mobile = isMobileDevice(userAgent);
const showBorea = !mobile && boreaSupported(userAgent);
const latestStable = computed(() =>
    props.mod.releases.find((release) => release.status === 'stable'),
);
// Pinned to the newest stable release; unpinned, Borea picks the newest itself.
const boreaInstall = computed(
    () =>
        (latestStable.value &&
            boreaInstallLink(props.mod.id, latestStable.value.version)) ??
        boreaInstallLink(props.mod.id),
);
const boreaOpen = computed(() => boreaModLink(props.mod.id));
// Each release with a Borea link pinned to it, or null when Borea could not read its version.
const releases = computed(() =>
    props.mod.releases.map((release) => ({
        ...release,
        boreaInstall: boreaInstallLink(props.mod.id, release.version),
    })),
);

// The game build the player runs, remembered from the mod list or picked here. Storage exists only
// in the browser, so it is read after mounting, where the server render cannot differ.
const gameVersion = ref<string | null>(null);
onMounted(() => {
    const remembered = rememberedGameVersion();
    gameVersion.value = props.gameVersions.some((v) => v.label === remembered)
        ? remembered
        : null;
});
function pickGameVersion(label: string) {
    gameVersion.value = label || null;
    rememberGameVersion(gameVersion.value);
}

const worksWith = (
    release: { game_min_revision: number | null },
    revision: number,
) =>
    release.game_min_revision === null || release.game_min_revision <= revision;

// Whether the release the install button gets runs on the player's game, and if not, the newest
// release that does: stable first, like the install button.
const compatibility = computed(() => {
    const game = props.gameVersions.find((v) => v.label === gameVersion.value);

    if (!game || !latest.value || latest.value.game_min_revision === null) {
        return null;
    }

    if (worksWith(latest.value, game.revision)) {
        return { works: true as const, game: game.label };
    }

    const fits = props.mod.releases.filter((r) => worksWith(r, game.revision));

    return {
        works: false as const,
        game: game.label,
        fallback: fits.find((r) => r.status === 'stable') ?? fits[0] ?? null,
    };
});

// The dependencies of the release the install button gets: its loader first, then by kind.
const dependencyKinds: Record<string, string> = {
    required: 'Required',
    recommends: 'Recommended',
    optional: 'Optional',
    suggests: 'Suggested',
    conflict: 'Conflicts with',
};
const dependencyGroups = computed(() => {
    const release = latest.value;

    if (!release) {
        return [];
    }

    const groups = Object.entries(dependencyKinds)
        .map(([kind, label]) => ({
            label,
            items: release.dependencies.filter((d) => d.kind === kind),
        }))
        .filter((group) => group.items.length > 0);

    return release.loader
        ? [
              {
                  label: 'Mod loader',
                  items: [
                      {
                          id: release.loader.id,
                          name: release.loader.name,
                          icon: release.loader.icon,
                          kind: 'loader',
                          min: release.loader.min,
                          max: null,
                      },
                  ],
              },
              ...groups,
          ]
        : groups;
});

// The mods that need this one, grouped the same way as its own dependencies.
const dependentGroups = computed(() =>
    [
        {
            label: 'As their mod loader',
            items: props.dependents.filter((d) => d.kind === 'loader'),
        },
        {
            label: 'As a required dependency',
            items: props.dependents.filter((d) => d.kind === 'required'),
        },
    ].filter((group) => group.items.length > 0),
);

// Everything about the mod shares one spot; a mod without a description skips that tab.
type Tab =
    | 'description'
    | 'gallery'
    | 'dependencies'
    | 'dependents'
    | 'releases'
    | 'reviews'
    | 'comments';
const tabs = computed(() => [
    ...(props.mod.description
        ? [
              {
                  id: 'description' as const,
                  label: 'Description',
                  count: undefined,
              },
          ]
        : []),
    ...(props.mod.images.length
        ? [
              {
                  id: 'gallery' as const,
                  label: 'Gallery',
                  count: props.mod.images.length,
              },
          ]
        : []),
    {
        id: 'dependencies' as const,
        label: 'Dependencies',
        count: dependencyGroups.value.reduce((n, g) => n + g.items.length, 0),
    },
    ...(props.dependents.length
        ? [
              {
                  id: 'dependents' as const,
                  label: 'Required by',
                  count: props.dependents.length,
              },
          ]
        : []),
    {
        id: 'releases' as const,
        label: 'Releases',
        count: props.mod.releases.length,
    },
    { id: 'reviews' as const, label: 'Reviews', count: props.reviews.count },
    { id: 'comments' as const, label: 'Comments', count: props.comments.count },
]);
// A link can name the tab to open, such as a notification's link to a comment.
const activeTab = ref<Tab>(
    tabs.value.find((tab) => tab.id === props.tab)?.id ??
        (props.mod.description ? 'description' : 'releases'),
);

// Then scroll to the comment it names. With server rendering the browser does this itself; this
// covers a page the browser renders.
onMounted(() => {
    if (location.hash.startsWith('#comment-')) {
        document
            .getElementById(location.hash.slice(1))
            ?.scrollIntoView({ block: 'center' });
    }
});
const tabButtons = useTemplateRef<HTMLButtonElement[]>('tabButtons');

// Arrow keys, Home and End move between tabs, per the WAI-ARIA tabs pattern.
function moveBetweenTabs(event: KeyboardEvent, index: number) {
    const last = tabs.value.length - 1;
    const target = {
        ArrowRight: index === last ? 0 : index + 1,
        ArrowLeft: index === 0 ? last : index - 1,
        Home: 0,
        End: last,
    }[event.key];

    if (target === undefined) {
        return;
    }

    event.preventDefault();
    activeTab.value = tabs.value[target].id;
    tabButtons.value?.[target]?.focus();
}

const linkLabels: Record<string, string> = {
    homepage: 'Website',
    repository: 'Source code',
    spacedock: 'SpaceDock',
    forums: 'Forum thread',
    discussions: 'Discussions',
    bugtracker: 'Report an issue',
};

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const primaryButton =
    'rounded-md bg-[#1b1b18] px-5 py-2.5 text-center text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
const menuItem =
    'block px-3 py-2 hover:bg-[#f4f3ef] focus-visible:bg-[#f4f3ef] focus-visible:outline-none dark:hover:bg-[#232321] dark:focus-visible:bg-[#232321]';

// The install button's menu closes on Escape, which returns focus to its toggle, and on a click outside.
const installMenu = useTemplateRef<HTMLElement>('installMenu');
const installMenuToggle =
    useTemplateRef<HTMLButtonElement>('installMenuToggle');
const installMenuOpen = ref(false);

function closeInstallMenu(refocus = false) {
    if (installMenuOpen.value && refocus) {
        installMenuToggle.value?.focus();
    }

    installMenuOpen.value = false;
}

function closeOnOutsideClick(event: PointerEvent) {
    if (!installMenu.value?.contains(event.target as Node)) {
        closeInstallMenu();
    }
}

onMounted(() => document.addEventListener('pointerdown', closeOnOutsideClick));
onBeforeUnmount(() =>
    document.removeEventListener('pointerdown', closeOnOutsideClick),
);
const card =
    'rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <PageMeta
        :title="`${mod.name} - KSA mod`"
        :json-ld="jsonLd"
        :description="
            mod.abstract ??
            `${mod.name}, a Kitten Space Agency mod${mod.authors.length ? ` by ${mod.authors.join(', ')}` : ''}.`
        "
        :image="mod.icon"
    />
    <Head>
        <link
            rel="alternate"
            type="application/atom+xml"
            :title="`${mod.name} releases`"
            :href="modFeed({ id: mod.id, format: 'atom' }).url"
        />
    </Head>

    <AppLayout>
        <Link :href="index()" :class="['text-sm hover:underline', muted]">
            ← All mods
        </Link>

        <div
            class="mt-4 mb-8 flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between"
        >
            <div class="flex gap-4">
                <ModIcon
                    :name="mod.name"
                    :src="mod.icon"
                    class="size-20 text-3xl"
                />
                <div>
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ mod.name }}
                    </h1>
                    <p :class="['text-sm', muted]">
                        by {{ mod.authors.join(', ') }}
                    </p>
                    <button
                        v-if="reviews.average !== null"
                        type="button"
                        class="mt-1 flex items-center gap-1.5 text-sm hover:underline"
                        @click="activeTab = 'reviews'"
                    >
                        <Stars :rating="reviews.average" />
                        <span>{{ reviews.average }}</span>
                        <span :class="muted"
                            >· {{ reviews.count }}
                            {{
                                reviews.count === 1 ? 'review' : 'reviews'
                            }}</span
                        >
                    </button>
                    <p
                        class="mt-2 max-w-2xl text-[#4a4a46] dark:text-[#c4c3be]"
                    >
                        {{ mod.abstract }}
                    </p>
                    <FollowButton
                        kind="mod"
                        :id="mod.id"
                        :following="following"
                        class="mt-3"
                    />
                </div>
            </div>

            <div v-if="latest" class="flex shrink-0 flex-col sm:items-end">
                <!-- Borea first where it runs; the direct download stays in its menu. -->
                <div
                    v-if="showBorea && boreaInstall"
                    ref="installMenu"
                    class="relative flex"
                    @keydown.escape="closeInstallMenu(true)"
                >
                    <a
                        :href="boreaInstall"
                        :class="cn(primaryButton, 'flex-1 rounded-r-none')"
                    >
                        Install<template v-if="latestStable">
                            v{{ latestStable.version }}</template
                        >
                        with Borea
                    </a>
                    <button
                        ref="installMenuToggle"
                        type="button"
                        :aria-expanded="installMenuOpen"
                        aria-controls="install-options"
                        aria-label="Other ways to get this mod"
                        :class="
                            cn(
                                primaryButton,
                                'rounded-l-none border-l border-white/20 px-2.5 dark:border-black/15',
                            )
                        "
                        @click="installMenuOpen = !installMenuOpen"
                    >
                        <svg
                            viewBox="0 0 16 16"
                            fill="currentColor"
                            aria-hidden="true"
                            :class="[
                                'size-4 transition-transform',
                                installMenuOpen && 'rotate-180',
                            ]"
                        >
                            <path
                                d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z"
                            />
                        </svg>
                    </button>

                    <ul
                        v-show="installMenuOpen"
                        id="install-options"
                        class="absolute top-full right-0 z-10 mt-1 w-full min-w-64 rounded-md bg-white py-1 text-sm shadow-[0_8px_24px_rgba(0,0,0,0.12),inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[0_8px_24px_rgba(0,0,0,0.5),inset_0px_0px_0px_1px_#fffaed2d]"
                    >
                        <li>
                            <a :href="latest.download_url" :class="menuItem">
                                Download v{{ latest.version }}
                                <span :class="['block text-xs', muted]"
                                    >Manual install<template
                                        v-if="latest.download_size"
                                    >
                                        ·
                                        {{
                                            formatBytes(latest.download_size)
                                        }}</template
                                    ></span
                                >
                            </a>
                        </li>
                        <li v-if="boreaOpen">
                            <a :href="boreaOpen" :class="menuItem">
                                Open in Borea
                            </a>
                        </li>
                        <li
                            class="mt-1 border-t border-[#19140014] pt-1 dark:border-[#3E3E3A]/60"
                        >
                            <a
                                :href="BOREA_RELEASES_URL"
                                target="_blank"
                                rel="noopener noreferrer"
                                :class="[menuItem, muted]"
                            >
                                Don't have Borea? Get it
                            </a>
                        </li>
                    </ul>
                </div>
                <a
                    v-else-if="!mobile"
                    :href="latest.download_url"
                    :class="primaryButton"
                >
                    Download v{{ latest.version }}
                </a>
                <div
                    v-else
                    class="rounded-md border border-dashed border-[#19140035] px-4 py-3 text-sm sm:max-w-72 sm:text-right dark:border-[#3E3E3A]"
                >
                    <p class="font-medium">
                        Downloads and installs are desktop only
                    </p>
                    <p :class="['mt-0.5 text-xs', muted]">
                        Open this page on a Windows, Linux or macOS computer to
                        get {{ mod.name }}.
                    </p>
                </div>

                <p :class="['mt-2 text-xs', muted]">
                    <template
                        v-if="
                            !mobile &&
                            !(showBorea && boreaInstall) &&
                            latest.download_size
                        "
                        >{{ formatBytes(latest.download_size) }} ·
                    </template>
                    <template v-if="latest.game_min"
                        >Game {{ latest.game_min }} or newer</template
                    >
                </p>
                <p
                    v-if="compatibility?.works"
                    class="mt-1 flex items-center gap-1 text-xs text-accent"
                >
                    <svg
                        viewBox="0 0 16 16"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="2"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                        class="size-3.5"
                    >
                        <path d="m3.5 8.5 3 3 6-7" />
                    </svg>
                    Works with your game ({{ compatibility.game }})
                </p>
                <p
                    v-else-if="compatibility"
                    class="mt-1 max-w-64 text-xs sm:text-right"
                >
                    <span class="font-medium"
                        >Needs a newer game than yours ({{
                            compatibility.game
                        }}).</span
                    >
                    <template v-if="compatibility.fallback">
                        <button
                            type="button"
                            class="underline underline-offset-4"
                            @click="activeTab = 'releases'"
                        >
                            v{{ compatibility.fallback.version }}
                        </button>
                        works with it.
                    </template>
                </p>
            </div>
        </div>

        <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div class="min-w-0">
                <div
                    role="tablist"
                    aria-label="About this mod"
                    class="mb-3 flex [scrollbar-width:none] gap-1 overflow-x-auto shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_rgba(62,62,58,0.6)]"
                >
                    <!-- The bottom line is an inset shadow, which the active tab's border paints over.
                         A border would need the tabs to overlap it, and inside a scroller that overlap scrolls. -->
                    <button
                        v-for="(tab, index) in tabs"
                        :id="`tab-${tab.id}`"
                        ref="tabButtons"
                        :key="tab.id"
                        type="button"
                        role="tab"
                        :aria-selected="activeTab === tab.id"
                        :aria-controls="`panel-${tab.id}`"
                        :tabindex="activeTab === tab.id ? 0 : -1"
                        :class="[
                            'shrink-0 border-b-2 px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors',
                            activeTab === tab.id
                                ? 'border-[#1b1b18] dark:border-[#EDEDEC]'
                                : [
                                      'border-transparent hover:text-[#1b1b18] dark:hover:text-[#EDEDEC]',
                                      muted,
                                  ],
                        ]"
                        @click="activeTab = tab.id"
                        @keydown="moveBetweenTabs($event, index)"
                    >
                        {{ tab.label }}
                        <span
                            v-if="tab.count !== undefined"
                            :class="['ml-1 text-xs font-normal', muted]"
                            >{{ tab.count }}</span
                        >
                    </button>
                </div>

                <!-- Rendered server-side from the index with raw HTML stripped. -->
                <article
                    v-if="mod.description"
                    v-show="activeTab === 'description'"
                    id="panel-description"
                    role="tabpanel"
                    aria-labelledby="tab-description"
                    tabindex="0"
                    :class="['markdown', card]"
                    v-html="mod.description"
                />

                <section
                    v-if="mod.images.length"
                    v-show="activeTab === 'gallery'"
                    id="panel-gallery"
                    role="tabpanel"
                    aria-labelledby="tab-gallery"
                    tabindex="0"
                >
                    <Gallery :images="mod.images" :name="mod.name" />
                </section>

                <section
                    v-show="activeTab === 'dependencies'"
                    id="panel-dependencies"
                    role="tabpanel"
                    aria-labelledby="tab-dependencies"
                    tabindex="0"
                    :class="card"
                >
                    <template v-if="dependencyGroups.length">
                        <p v-if="latest" :class="['mb-4 text-xs', muted]">
                            For v{{ latest.version }}, the release the install
                            button gets.
                        </p>
                        <ModRelationGroups :groups="dependencyGroups" />
                    </template>
                    <p v-else :class="['text-sm', muted]">
                        This mod needs nothing besides the game.
                    </p>
                </section>

                <section
                    v-if="dependents.length"
                    v-show="activeTab === 'dependents'"
                    id="panel-dependents"
                    role="tabpanel"
                    aria-labelledby="tab-dependents"
                    tabindex="0"
                    :class="card"
                >
                    <p :class="['mb-4 text-xs', muted]">
                        Mods whose newest release needs {{ mod.name }}, most
                        downloaded first.
                    </p>
                    <ModRelationGroups :groups="dependentGroups" />
                    <Link
                        v-if="mod.type === 'mod-loader'"
                        :href="`${index().url}?loader=${encodeURIComponent(mod.id)}`"
                        :class="[
                            'mt-4 inline-block text-xs underline underline-offset-4',
                            muted,
                        ]"
                    >
                        Browse mods for {{ mod.name }}
                    </Link>
                </section>

                <section
                    v-show="activeTab === 'releases'"
                    id="panel-releases"
                    role="tabpanel"
                    aria-labelledby="tab-releases"
                    tabindex="0"
                    :class="card"
                >
                    <ul
                        class="divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                    >
                        <li
                            v-for="release in releases"
                            :key="release.version"
                            class="py-3 first:pt-0 last:pb-0"
                        >
                            <div
                                class="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm"
                            >
                                <span class="font-medium">{{
                                    release.version
                                }}</span>
                                <span
                                    v-if="release.status !== 'stable'"
                                    class="rounded bg-accent-wash px-1.5 py-0.5 text-xs text-accent"
                                >
                                    {{ release.status }}
                                </span>
                                <span :class="['text-xs', muted]">{{
                                    formatDate(release.date)
                                }}</span>
                                <a
                                    v-if="!mobile"
                                    :href="release.download_url"
                                    class="ml-auto text-xs underline underline-offset-4"
                                >
                                    Download<template
                                        v-if="release.download_size"
                                    >
                                        ({{
                                            formatBytes(release.download_size)
                                        }})</template
                                    >
                                </a>
                                <a
                                    v-if="showBorea && release.boreaInstall"
                                    :href="release.boreaInstall"
                                    class="text-xs underline underline-offset-4"
                                >
                                    Install with Borea
                                </a>
                            </div>
                            <details v-if="release.changelog" class="mt-1">
                                <summary
                                    :class="['cursor-pointer text-xs', muted]"
                                >
                                    Changelog
                                </summary>
                                <div
                                    class="markdown mt-2 text-sm"
                                    v-html="release.changelog"
                                />
                            </details>
                        </li>
                    </ul>
                </section>

                <section
                    v-show="activeTab === 'reviews'"
                    id="panel-reviews"
                    role="tabpanel"
                    aria-labelledby="tab-reviews"
                    tabindex="0"
                    :class="card"
                >
                    <ReviewsPanel
                        :mod-id="mod.id"
                        :reviews="reviews"
                        :signed-in="!!page.props.auth.user"
                    />
                </section>

                <section
                    v-show="activeTab === 'comments'"
                    id="panel-comments"
                    role="tabpanel"
                    aria-labelledby="tab-comments"
                    tabindex="0"
                    :class="card"
                >
                    <CommentsPanel
                        :mod-id="mod.id"
                        :comments="comments"
                        :signed-in="!!page.props.auth.user"
                    />
                </section>
            </div>

            <!-- Sticky beside the content on wide screens; self-start, or the grid stretches it and it cannot stick. -->
            <aside
                class="flex flex-col gap-6 text-sm lg:sticky lg:top-6 lg:max-h-[calc(100vh-3rem)] lg:self-start lg:overflow-y-auto"
            >
                <section :class="card">
                    <h2 class="mb-3 font-semibold">Details</h2>
                    <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
                        <dt :class="muted">Downloads</dt>
                        <dd>{{ formatNumber(mod.downloads) }}</dd>
                        <dt :class="muted">Updated</dt>
                        <dd>{{ formatDate(mod.updated_at) }}</dd>
                        <dt :class="muted">Published</dt>
                        <dd>{{ formatDate(mod.published_at) }}</dd>
                        <template v-if="mod.license">
                            <dt :class="muted">License</dt>
                            <dd>{{ mod.license }}</dd>
                        </template>
                        <template v-if="latest?.loader">
                            <dt :class="muted">Loader</dt>
                            <dd>
                                {{ latest.loader.id }}
                                <template v-if="latest.loader.min"
                                    >{{ latest.loader.min }}+</template
                                >
                            </dd>
                        </template>
                        <template v-if="gameVersions.length">
                            <dt :class="muted">
                                <label for="game-version">Your game</label>
                            </dt>
                            <dd>
                                <select
                                    id="game-version"
                                    :value="gameVersion ?? ''"
                                    class="w-full rounded-md border border-[#19140035] bg-white px-1.5 py-0.5 text-sm dark:border-[#3E3E3A] dark:bg-[#161615]"
                                    @change="
                                        pickGameVersion(
                                            ($event.target as HTMLSelectElement)
                                                .value,
                                        )
                                    "
                                >
                                    <option value="">Not set</option>
                                    <option
                                        v-for="version in gameVersions"
                                        :key="version.label"
                                        :value="version.label"
                                    >
                                        {{ version.label }}
                                    </option>
                                </select>
                            </dd>
                        </template>
                    </dl>

                    <ul
                        v-if="mod.tags.length"
                        class="mt-4 flex flex-wrap gap-2"
                    >
                        <li v-for="tag in mod.tags" :key="tag">
                            <Link
                                :href="`${index().url}?tag=${encodeURIComponent(tag)}`"
                                class="block rounded-full border border-[#19140035] px-2.5 py-0.5 text-xs hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                            >
                                {{ tagName(tag, tagNames) }}
                            </Link>
                        </li>
                    </ul>

                    <p class="mt-4 flex flex-wrap gap-x-4 gap-y-1 text-xs">
                        <!-- A plain link: guests get redirected to GitHub to log in. -->
                        <a
                            :href="edit(mod.id).url"
                            :class="['underline underline-offset-4', muted]"
                        >
                            Edit this listing
                        </a>
                        <a
                            :href="modFeed({ id: mod.id, format: 'atom' }).url"
                            :class="['underline underline-offset-4', muted]"
                        >
                            Releases feed
                        </a>
                    </p>
                </section>

                <section v-if="Object.keys(mod.links).length" :class="card">
                    <h2 class="mb-3 font-semibold">Links</h2>
                    <ul class="flex flex-col gap-2">
                        <li v-for="(url, key) in mod.links" :key="key">
                            <a
                                :href="url"
                                target="_blank"
                                rel="noopener noreferrer"
                                class="underline underline-offset-4"
                            >
                                {{ linkLabels[key] ?? key }}
                            </a>
                        </li>
                    </ul>
                </section>
            </aside>
        </div>
    </AppLayout>
</template>
