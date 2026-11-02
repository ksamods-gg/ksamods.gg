<script setup lang="ts">
import { Link, router, useForm, usePage } from '@inertiajs/vue3';
import { computed, ref, useTemplateRef } from 'vue';
import FollowButton from '@/components/FollowButton.vue';
import ModIcon from '@/components/ModIcon.vue';
import Gallery from '@/components/mods/Gallery.vue';
import ModRelationGroups from '@/components/mods/ModRelationGroups.vue';
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { isMobileDevice } from '@/lib/device';
import { formatBytes, formatDate, formatNumber } from '@/lib/format';
import { t } from '@/lib/i18n';
import { create, destroy, edit, index, takeDown } from '@/routes/shares';
import {
    cover as coverImage,
    destroy as destroyImage,
    store as storeImages,
} from '@/routes/shares/images';
import { downloads as countDownload } from '@/routes/share/api';
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

// Counted on click rather than at the file's url, which a CDN caches and clients fetch for
// thumbnails. keepalive lets the count finish while the browser follows the download.
function counted(): void {
    fetch(countDownload({ type: kind, id: props.share.id }).url, {
        method: 'POST',
        keepalive: true,
    }).catch(() => {});
}

// A vehicle or save goes into the game's folders on a desktop, so phones get a note instead.
const mobile = isMobileDevice(page.props.userAgent);

// The same groups as a mod's dependencies.
const dependencyKinds = computed<Record<string, string>>(() => ({
    required: t('Required'),
    recommends: t('Recommended'),
    optional: t('Optional'),
    suggests: t('Suggested'),
    conflict: t('Conflicts with'),
}));
const dependencyGroups = computed(() =>
    Object.entries(dependencyKinds.value)
        .map(([kind, label]) => ({
            label,
            items: props.share.dependencies.filter((d) => d.kind === kind),
        }))
        .filter((group) => group.items.length > 0),
);

type Tab = 'description' | 'screenshots' | 'dependencies' | 'releases';
const tabs = computed(() => [
    // The owner sees the tab without screenshots too, to add the first.
    ...(props.share.images.length || props.owns
        ? [
              {
                  id: 'screenshots' as const,
                  label: t('Screenshots'),
                  count: props.share.images.length,
              },
          ]
        : []),
    ...(props.share.description
        ? [
              {
                  id: 'description' as const,
                  label: t('Description'),
                  count: undefined,
              },
          ]
        : []),
    {
        id: 'dependencies' as const,
        label: t('Dependencies'),
        count: props.share.dependencies.length,
    },
    {
        id: 'releases' as const,
        label: t('Releases'),
        count: props.share.releases.length,
    },
]);
const activeTab = ref<Tab>(
    props.share.images.length
        ? 'screenshots'
        : props.share.description
          ? 'description'
          : 'dependencies',
);

// Screenshots: the owner adds them and picks the cover; the owner or a moderator removes one.
const uploadForm = useForm<{ images: File[] }>({ images: [] });

function addScreenshots(event: Event) {
    const input = event.target as HTMLInputElement;
    uploadForm.images = [...(input.files ?? [])];
    uploadForm.post(storeImages({ types: listing, id: props.share.id }).url, {
        preserveScroll: true,
        forceFormData: true,
        onFinish: () => (input.value = ''),
    });
}

function removeScreenshot(id: number) {
    if (
        confirm(t('Remove this screenshot? It also leaves the index listing.'))
    ) {
        router.delete(destroyImage(id).url, { preserveScroll: true });
    }
}
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
            t(
                'Take down :name :version? Its archive is deleted and its download answers that it is gone. This cannot be undone.',
                { name: props.share.name, version },
            ),
        )
    ) {
        router.post(takeDown(id).url, {}, { preserveScroll: true });
    }
}

function deleteListing() {
    if (
        confirm(
            t(
                'Delete :name for good? Its releases, files and pull request go, and its id becomes free again.',
                { name: props.share.name },
            ),
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
        :title="
            share.type === 'vehicle'
                ? $t(':name - KSA vehicle', { name: share.name })
                : $t(':name - KSA save', { name: share.name })
        "
        :description="share.abstract"
        :json-ld="jsonLd"
    />

    <AppLayout>
        <Link
            :href="index(listing)"
            :class="['text-sm hover:underline', muted]"
        >
            ←
            {{ listing === 'vehicles' ? $t('All vehicles') : $t('All saves') }}
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
                    :src="share.icon"
                    class="size-20 text-3xl"
                />
                <div>
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ share.name }}
                    </h1>
                    <p :class="['text-sm', muted]">
                        {{
                            share.type === 'vehicle'
                                ? $t('Vehicle by :authors', {
                                      authors: share.authors.join(', '),
                                  })
                                : $t('Save by :authors', {
                                      authors: share.authors.join(', '),
                                  })
                        }}
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
                    @click="counted"
                >
                    {{ $t('Download v:version', { version: latest.version }) }}
                </a>
                <div
                    v-else-if="mobile"
                    class="rounded-md border border-dashed border-[#19140035] px-4 py-3 text-sm sm:max-w-72 sm:text-right dark:border-[#3E3E3A]"
                >
                    <p class="font-medium">
                        {{ $t('Downloads are desktop only') }}
                    </p>
                    <p :class="['mt-0.5 text-xs', muted]">
                        {{
                            $t(
                                'Open this page on a Windows, Linux or macOS computer to get :name.',
                                { name: share.name },
                            )
                        }}
                    </p>
                </div>

                <p :class="['mt-2 text-xs', muted]">
                    <template v-if="!mobile"
                        >{{ formatBytes(latest.size) }} ·
                    </template>
                    {{
                        $t('Game :version or newer', {
                            version: latest.game_version,
                        })
                    }}
                </p>
                <p
                    v-if="!mobile"
                    :class="['mt-1 max-w-64 text-xs sm:text-right', muted]"
                >
                    {{
                        share.type === 'vehicle'
                            ? $t(
                                  'A mod manager such as Borea installs the mods it needs first. By hand, unzip it into your Vehicles folder.',
                              )
                            : $t(
                                  'A mod manager such as Borea installs the mods it needs first. By hand, unzip it into your saves folder.',
                              )
                    }}
                </p>
            </div>
        </div>

        <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
            <div class="min-w-0">
                <div
                    role="tablist"
                    :aria-label="
                        kind === 'vehicle'
                            ? $t('About this vehicle')
                            : $t('About this save')
                    "
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
                    v-if="share.images.length || owns"
                    v-show="activeTab === 'screenshots'"
                    id="panel-screenshots"
                    role="tabpanel"
                    aria-labelledby="tab-screenshots"
                    tabindex="0"
                    class="flex flex-col gap-4"
                >
                    <Gallery
                        v-if="share.images.length"
                        :images="share.images.map((image) => image.url)"
                        :name="share.name"
                    />

                    <div
                        v-if="owns || canTakeDown"
                        :class="[card, 'flex flex-col gap-3']"
                    >
                        <p class="text-sm font-semibold">
                            {{
                                owns
                                    ? $t('Your screenshots')
                                    : $t('Moderate screenshots')
                            }}
                        </p>
                        <p v-if="owns" :class="['text-xs', muted]">
                            {{
                                $t(
                                    'Up to 6 PNG, JPEG or WebP images, at most 10 MB each, converted to WebP. The first is the cover and its square middle is the icon. Changes go to the index by pull request.',
                                )
                            }}
                        </p>
                        <ul
                            v-if="share.images.length"
                            class="grid grid-cols-2 gap-3 sm:grid-cols-3"
                        >
                            <li
                                v-for="(image, index) in share.images"
                                :key="image.id"
                                class="flex flex-col gap-1.5"
                            >
                                <img
                                    :src="image.url"
                                    :alt="
                                        $t('Screenshot :number', {
                                            number: index + 1,
                                        })
                                    "
                                    class="aspect-video w-full rounded-md object-cover"
                                />
                                <span class="flex flex-wrap gap-x-3 text-xs">
                                    <span
                                        v-if="index === 0"
                                        class="text-accent"
                                        >{{ $t('Cover') }}</span
                                    >
                                    <Link
                                        v-else-if="owns"
                                        :href="coverImage(image.id)"
                                        as="button"
                                        preserve-scroll
                                        class="underline underline-offset-4"
                                        >{{ $t('Make cover') }}</Link
                                    >
                                    <button
                                        type="button"
                                        class="text-[#f53003] hover:underline dark:text-[#FF4433]"
                                        @click="removeScreenshot(image.id)"
                                    >
                                        {{ $t('Remove') }}
                                    </button>
                                </span>
                            </li>
                        </ul>
                        <label
                            v-if="owns && share.images.length < 6"
                            class="cursor-pointer self-start rounded-md border border-[#19140035] px-3 py-1.5 text-sm hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                        >
                            {{
                                uploadForm.processing
                                    ? $t('Uploading…')
                                    : $t('Add screenshots')
                            }}
                            <input
                                type="file"
                                accept="image/png,image/jpeg,image/webp"
                                multiple
                                class="sr-only"
                                :disabled="uploadForm.processing"
                                @change="addScreenshots"
                            />
                        </label>
                        <p
                            v-if="uploadForm.errors.images"
                            role="alert"
                            class="text-xs text-[#f53003] dark:text-[#FF4433]"
                        >
                            {{ uploadForm.errors.images }}
                        </p>
                    </div>
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
                        {{
                            kind === 'vehicle'
                                ? $t(
                                      'This vehicle needs nothing besides the game.',
                                  )
                                : $t(
                                      'This save needs nothing besides the game.',
                                  )
                        }}
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
                                >{{ formatDate(release.date) }} ·
                                {{
                                    $t('game :version', {
                                        version: release.game_version,
                                    })
                                }}</span
                            >
                            <span
                                class="ml-auto flex items-center gap-3 text-xs"
                            >
                                <span
                                    v-if="release.taken_down"
                                    :class="muted"
                                    >{{ $t('Taken down') }}</span
                                >
                                <a
                                    v-else-if="release.url && !mobile"
                                    :href="release.url"
                                    class="underline underline-offset-4"
                                    @click="counted"
                                    >{{
                                        $t('Download (:size)', {
                                            size: formatBytes(release.size),
                                        })
                                    }}</a
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
                                    {{ $t('Take down') }}
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
                    <h2 class="mb-3 font-semibold">{{ $t('Details') }}</h2>
                    <dl
                        class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2"
                    >
                        <dt :class="muted">{{ $t('Updated') }}</dt>
                        <dd>{{ formatDate(share.updated_at) }}</dd>
                        <dt :class="muted">{{ $t('Downloads') }}</dt>
                        <dd class="tabular-nums">
                            {{ formatNumber(share.downloads) }}
                        </dd>
                        <template v-if="share.game_version">
                            <dt :class="muted">{{ $t('Game') }}</dt>
                            <dd>
                                {{
                                    $t(':version or newer', {
                                        version: share.game_version,
                                    })
                                }}
                            </dd>
                        </template>
                        <dt :class="muted">{{ $t('License') }}</dt>
                        <dd>{{ share.license }}</dd>
                        <dt :class="muted">{{ $t('Id') }}</dt>
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
                        >{{ $t('Its listing in the index') }}</a
                    >
                </section>

                <section v-if="owns" :class="[card, 'flex flex-col gap-3']">
                    <h2 class="font-semibold">{{ $t('Your listing') }}</h2>
                    <Link
                        :href="create({ query: { listing: share.id } })"
                        :class="primaryButton"
                        >{{ $t('Upload a new version') }}</Link
                    >
                    <Link
                        :href="edit({ types: listing, id: share.id })"
                        :class="secondaryButton"
                        >{{ $t('Edit the listing') }}</Link
                    >
                    <template v-if="canDelete">
                        <button
                            type="button"
                            class="text-left text-xs text-[#f53003] hover:underline dark:text-[#FF4433]"
                            @click="deleteListing"
                        >
                            {{
                                kind === 'vehicle'
                                    ? $t('Delete this vehicle')
                                    : $t('Delete this save')
                            }}
                        </button>
                        <p :class="['text-xs', muted]">
                            {{
                                $t(
                                    'Possible until the index takes it. After that, take its releases down instead.',
                                )
                            }}
                        </p>
                    </template>
                </section>
            </aside>
        </div>
    </AppLayout>
</template>
