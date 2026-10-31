<script setup lang="ts">
import { Link, router, usePage } from '@inertiajs/vue3';
import { computed, ref, useTemplateRef } from 'vue';
import FollowButton from '@/components/FollowButton.vue';
import ModIcon from '@/components/ModIcon.vue';
import ModRelationGroups from '@/components/mods/ModRelationGroups.vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { isMobileDevice } from '@/lib/device';
import { formatBytes, formatDate } from '@/lib/format';
import { create, destroy, edit, index, takeDown } from '@/routes/shares';
import type { ShareDetail } from '@/types';

const props = defineProps<{
    share: ShareDetail;
    owns: boolean;
    following: boolean;
    canTakeDown: boolean;
    canDelete: boolean;
}>();

const page = usePage();
const listing = props.share.type === 'vehicle' ? 'vehicles' : 'saves';
const kind = props.share.type === 'vehicle' ? 'vehicle' : 'save';

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

// Newest first, so the first one still up is the one the download button gets.
const latest = computed(() =>
    props.share.releases.find((release) => !release.taken_down),
);

// A vehicle or save goes into the game's folders on a desktop, so phones get a note instead.
const mobile = isMobileDevice(page.props.userAgent);

// The same groups as a mod's dependencies.
const dependencyKinds: Record<string, string> = {
    required: 'Required',
    recommends: 'Recommended',
    optional: 'Optional',
    suggests: 'Suggested',
    conflict: 'Conflicts with',
};
const dependencyGroups = computed(() =>
    Object.entries(dependencyKinds)
        .map(([kind, label]) => ({
            label,
            items: props.share.dependencies.filter((d) => d.kind === kind),
        }))
        .filter((group) => group.items.length > 0),
);

type Tab = 'description' | 'dependencies' | 'releases';
const tabs = computed(() => [
    ...(props.share.description
        ? [
              {
                  id: 'description' as const,
                  label: 'Description',
                  count: undefined,
              },
          ]
        : []),
    {
        id: 'dependencies' as const,
        label: 'Dependencies',
        count: props.share.dependencies.length,
    },
    {
        id: 'releases' as const,
        label: 'Releases',
        count: props.share.releases.length,
    },
]);
const activeTab = ref<Tab>(
    props.share.description ? 'description' : 'dependencies',
);
const tabButtons = useTemplateRef<HTMLButtonElement[]>('tabButtons');

// Arrow keys, Home and End move between tabs, per the WAI-ARIA tabs pattern.
function moveBetweenTabs(event: KeyboardEvent, position: number) {
    const last = tabs.value.length - 1;
    const target = {
        ArrowRight: position === last ? 0 : position + 1,
        ArrowLeft: position === 0 ? last : position - 1,
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

function takeDownRelease(id: number, version: string) {
    if (
        confirm(
            `Take down ${props.share.name} ${version}? Its archive is deleted and its download answers that it is gone. This cannot be undone.`,
        )
    ) {
        router.post(takeDown(id).url, {}, { preserveScroll: true });
    }
}

function deleteListing() {
    if (
        confirm(
            `Delete ${props.share.name} for good? Its releases, files and pull request go, and its id becomes free again.`,
        )
    ) {
        router.delete(destroy({ types: listing, id: props.share.id }).url);
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const primaryButton =
    'rounded-md bg-[#1b1b18] px-5 py-2.5 text-center text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white';
const secondaryButton =
    'rounded-md border border-[#19140035] px-5 py-2.5 text-center text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
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
            ← All {{ listing }}
        </Link>

        <p
            v-if="page.flash.status"
            role="status"
            class="mt-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ page.flash.status }}
        </p>
        <p
            v-if="page.props.errors.listing"
            role="alert"
            class="mt-4 rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
        >
            {{ page.props.errors.listing }}
        </p>

        <div
            class="mt-4 mb-8 flex flex-col gap-6 sm:flex-row sm:items-start sm:justify-between"
        >
            <div class="flex gap-4">
                <ModIcon
                    :name="share.name"
                    :src="null"
                    class="size-20 text-3xl"
                />
                <div>
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ share.name }}
                    </h1>
                    <p :class="['text-sm', muted]">
                        {{ share.type === 'vehicle' ? 'Vehicle' : 'Save' }}
                        by {{ share.authors.join(', ') }}
                    </p>
                    <p
                        class="mt-2 max-w-2xl text-[#4a4a46] dark:text-[#c4c3be]"
                    >
                        {{ share.abstract }}
                    </p>
                    <FollowButton
                        :kind="share.type"
                        :id="share.id"
                        :following="following"
                        class="mt-3"
                    />
                </div>
            </div>

            <div v-if="latest" class="flex shrink-0 flex-col sm:items-end">
                <a
                    v-if="!mobile && latest.url"
                    :href="latest.url"
                    :class="primaryButton"
                >
                    Download v{{ latest.version }}
                </a>
                <div
                    v-else-if="mobile"
                    class="rounded-md border border-dashed border-[#19140035] px-4 py-3 text-sm sm:max-w-72 sm:text-right dark:border-[#3E3E3A]"
                >
                    <p class="font-medium">Downloads are desktop only</p>
                    <p :class="['mt-0.5 text-xs', muted]">
                        Open this page on a Windows, Linux or macOS computer to
                        get {{ share.name }}.
                    </p>
                </div>

                <p :class="['mt-2 text-xs', muted]">
                    <template v-if="!mobile"
                        >{{ formatBytes(latest.size) }} ·
                    </template>
                    Game {{ latest.game_version }} or newer
                </p>
                <p
                    v-if="!mobile"
                    :class="['mt-1 max-w-64 text-xs sm:text-right', muted]"
                >
                    A mod manager such as Borea installs the mods it needs
                    first. By hand, unzip it into your
                    {{ share.type === 'vehicle' ? 'Vehicles' : 'saves' }}
                    folder.
                </p>
            </div>
        </div>

        <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div class="min-w-0">
                <div
                    role="tablist"
                    :aria-label="`About this ${kind}`"
                    class="mb-3 flex [scrollbar-width:none] gap-1 overflow-x-auto shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_rgba(62,62,58,0.6)]"
                >
                    <button
                        v-for="(tab, position) in tabs"
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
                        @keydown="moveBetweenTabs($event, position)"
                    >
                        {{ tab.label }}
                        <span
                            v-if="tab.count !== undefined"
                            :class="['ml-1 text-xs font-normal', muted]"
                            >{{ tab.count }}</span
                        >
                    </button>
                </div>

                <section
                    v-if="share.description"
                    v-show="activeTab === 'description'"
                    id="panel-description"
                    role="tabpanel"
                    aria-labelledby="tab-description"
                    tabindex="0"
                    :class="card"
                >
                    <p class="text-sm break-words whitespace-pre-line">
                        {{ share.description }}
                    </p>
                </section>

                <section
                    v-show="activeTab === 'dependencies'"
                    id="panel-dependencies"
                    role="tabpanel"
                    aria-labelledby="tab-dependencies"
                    tabindex="0"
                    :class="card"
                >
                    <ModRelationGroups
                        v-if="dependencyGroups.length"
                        :groups="dependencyGroups"
                    />
                    <p v-else :class="['text-sm', muted]">
                        This {{ kind }} needs nothing besides the game.
                    </p>
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
                            v-for="release in share.releases"
                            :key="release.id"
                            class="flex flex-wrap items-center gap-x-3 gap-y-1 py-3 text-sm first:pt-0 last:pb-0"
                        >
                            <span class="font-medium">{{
                                release.version
                            }}</span>
                            <span
                                v-if="release.status !== 'stable'"
                                class="rounded bg-accent-wash px-1.5 py-0.5 text-xs text-accent"
                                >{{ release.status }}</span
                            >
                            <span :class="['text-xs', muted]"
                                >{{ formatDate(release.date) }} · game
                                {{ release.game_version }}</span
                            >
                            <span
                                class="ml-auto flex items-center gap-3 text-xs"
                            >
                                <span v-if="release.taken_down" :class="muted"
                                    >Taken down</span
                                >
                                <a
                                    v-else-if="release.url && !mobile"
                                    :href="release.url"
                                    class="underline underline-offset-4"
                                    >Download ({{
                                        formatBytes(release.size)
                                    }})</a
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

            <!-- Sticky beside the content on wide screens; self-start, or the grid stretches it and it cannot stick. -->
            <aside
                class="flex flex-col gap-6 text-sm lg:sticky lg:top-6 lg:self-start"
            >
                <section :class="card">
                    <h2 class="mb-3 font-semibold">Details</h2>
                    <dl
                        class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2"
                    >
                        <dt :class="muted">Updated</dt>
                        <dd>{{ formatDate(share.updated_at) }}</dd>
                        <template v-if="share.game_version">
                            <dt :class="muted">Game</dt>
                            <dd>{{ share.game_version }} or newer</dd>
                        </template>
                        <dt :class="muted">License</dt>
                        <dd>{{ share.license }}</dd>
                        <dt :class="muted">Id</dt>
                        <dd class="truncate font-mono">{{ share.id }}</dd>
                    </dl>

                    <ul
                        v-if="share.tags.length"
                        class="mt-4 flex flex-wrap gap-2"
                    >
                        <li
                            v-for="tag in share.tags"
                            :key="tag"
                            class="rounded-full border border-[#19140035] px-2.5 py-0.5 text-xs dark:border-[#3E3E3A]"
                        >
                            {{ tag }}
                        </li>
                    </ul>

                    <a
                        v-if="share.pull_request_url"
                        :href="share.pull_request_url"
                        target="_blank"
                        rel="noopener noreferrer"
                        :class="[
                            'mt-4 inline-block text-xs underline underline-offset-4',
                            muted,
                        ]"
                        >Its listing in the index</a
                    >
                </section>

                <section v-if="owns" :class="[card, 'flex flex-col gap-3']">
                    <h2 class="font-semibold">Your listing</h2>
                    <Link
                        :href="create({ query: { listing: share.id } })"
                        :class="primaryButton"
                        >Upload a new version</Link
                    >
                    <Link
                        :href="edit({ types: listing, id: share.id })"
                        :class="secondaryButton"
                        >Edit the listing</Link
                    >
                    <template v-if="canDelete">
                        <button
                            type="button"
                            class="text-left text-xs text-[#f53003] hover:underline dark:text-[#FF4433]"
                            @click="deleteListing"
                        >
                            Delete this {{ kind }}
                        </button>
                        <p :class="['text-xs', muted]">
                            Possible until the index takes it. After that, take
                            its releases down instead.
                        </p>
                    </template>
                </section>
            </aside>
        </div>
    </AppLayout>
</template>
