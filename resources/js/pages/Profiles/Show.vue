<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import { computed, ref, useTemplateRef } from 'vue';
import ModIcon from '@/components/ModIcon.vue';
import ModRow from '@/components/ModRow.vue';
import PageMeta from '@/components/PageMeta.vue';
import ShareRow from '@/components/ShareRow.vue';
import Stars from '@/components/Stars.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate } from '@/lib/format';
import type { ModSummary, ShareSummary } from '@/types';

type ProfilePost = {
    id: number;
    rating: number | null;
    body: string;
    mod: { id: string; name: string };
    url: string;
    created_at: string;
};

const props = defineProps<{
    profile: {
        name: string;
        username: string;
        avatar: string | null;
        bio: string | null;
        links: string[];
        github: string | null;
        discord: string | null;
        joined: string | null;
    };
    // Null for a section the player keeps off their profile.
    vehicles: ShareSummary[] | null;
    saves: ShareSummary[] | null;
    mods: ModSummary[] | null;
    reviews: ProfilePost[] | null;
    comments: ProfilePost[] | null;
    tagNames: Record<string, string>;
}>();

type Tab = 'mods' | 'vehicles' | 'saves' | 'reviews' | 'comments';
const tabs = computed(() =>
    (
        [
            { id: 'mods', label: 'Mods', items: props.mods },
            { id: 'vehicles', label: 'Vehicles', items: props.vehicles },
            { id: 'saves', label: 'Saves', items: props.saves },
            { id: 'reviews', label: 'Reviews', items: props.reviews },
            { id: 'comments', label: 'Comments', items: props.comments },
        ] as const
    )
        .filter((tab) => tab.items !== null)
        .map((tab) => ({
            id: tab.id as Tab,
            label: tab.label,
            count: tab.items!.length,
        })),
);
// The first section with something in it.
const activeTab = ref<Tab | undefined>(
    tabs.value.find((tab) => tab.count > 0)?.id ?? tabs.value[0]?.id,
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

const posts = computed(() =>
    activeTab.value === 'reviews'
        ? props.reviews
        : activeTab.value === 'comments'
          ? props.comments
          : null,
);

// The address without its scheme, as a link's label.
const linkLabel = (url: string) =>
    url.replace(/^https?:\/\//, '').replace(/\/$/, '');

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const empty = [
    'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
    muted,
];
</script>

<template>
    <PageMeta
        :title="`${profile.name} (@${profile.username})`"
        :description="
            profile.bio ??
            `${profile.name} on ksamods.gg, the Kitten Space Agency mod site.`
        "
        :image="profile.avatar"
    />

    <AppLayout>
        <div
            class="mx-auto grid max-w-5xl gap-8 md:grid-cols-[16rem_minmax(0,1fr)]"
        >
            <aside class="flex flex-col gap-4">
                <img
                    v-if="profile.avatar"
                    :src="profile.avatar"
                    alt=""
                    class="size-28 rounded-full md:size-56"
                />
                <ModIcon
                    v-else
                    :name="profile.name"
                    :src="null"
                    class="size-28 rounded-full text-4xl md:size-56 md:text-6xl"
                />
                <div>
                    <h1 class="text-2xl font-semibold tracking-tight">
                        {{ profile.name }}
                    </h1>
                    <p :class="['text-lg', muted]">@{{ profile.username }}</p>
                </div>
                <p
                    v-if="profile.bio"
                    class="text-sm break-words whitespace-pre-line"
                >
                    {{ profile.bio }}
                </p>
                <ul :class="['flex flex-col gap-1.5 text-sm', muted]">
                    <li v-for="link in profile.links" :key="link">
                        <a
                            :href="link"
                            target="_blank"
                            rel="noopener noreferrer nofollow ugc"
                            class="break-all text-[#1b1b18] hover:underline dark:text-[#EDEDEC]"
                            >{{ linkLabel(link) }}</a
                        >
                    </li>
                    <li v-if="profile.github">
                        GitHub:
                        <a
                            :href="`https://github.com/${profile.github}`"
                            target="_blank"
                            rel="noopener noreferrer"
                            class="text-[#1b1b18] hover:underline dark:text-[#EDEDEC]"
                            >{{ profile.github }}</a
                        >
                    </li>
                    <li v-if="profile.discord">
                        Discord:
                        <span class="text-[#1b1b18] dark:text-[#EDEDEC]">{{
                            profile.discord
                        }}</span>
                    </li>
                    <li v-if="profile.joined">
                        Joined {{ formatDate(profile.joined) }}
                    </li>
                </ul>
            </aside>

            <div class="min-w-0">
                <p v-if="!tabs.length" :class="empty">
                    {{ profile.name }} keeps everything off their profile.
                </p>

                <template v-else>
                    <div
                        role="tablist"
                        :aria-label="`${profile.name}'s content`"
                        class="mb-4 flex [scrollbar-width:none] gap-1 overflow-x-auto shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_rgba(62,62,58,0.6)]"
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
                                :class="['ml-1 text-xs font-normal', muted]"
                                >{{ tab.count }}</span
                            >
                        </button>
                    </div>

                    <section
                        :id="`panel-${activeTab}`"
                        role="tabpanel"
                        :aria-labelledby="`tab-${activeTab}`"
                    >
                        <template v-if="activeTab === 'mods'">
                            <ul v-if="mods?.length" class="flex flex-col gap-3">
                                <li v-for="mod in mods" :key="mod.id">
                                    <ModRow :mod="mod" :tag-names="tagNames" />
                                </li>
                            </ul>
                            <p v-else :class="empty">No mods yet.</p>
                        </template>

                        <template
                            v-else-if="
                                activeTab === 'vehicles' ||
                                activeTab === 'saves'
                            "
                        >
                            <ul
                                v-if="
                                    (activeTab === 'vehicles'
                                        ? vehicles
                                        : saves
                                    )?.length
                                "
                                class="flex flex-col gap-3"
                            >
                                <li
                                    v-for="share in activeTab === 'vehicles'
                                        ? vehicles
                                        : saves"
                                    :key="share.id"
                                >
                                    <ShareRow :share="share" />
                                </li>
                            </ul>
                            <p v-else :class="empty">
                                No {{ activeTab }} shared yet.
                            </p>
                        </template>

                        <template v-else>
                            <ul
                                v-if="posts?.length"
                                class="flex flex-col gap-3"
                            >
                                <li
                                    v-for="post in posts"
                                    :key="post.id"
                                    :class="card"
                                >
                                    <p
                                        class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm"
                                    >
                                        <Link
                                            :href="post.url"
                                            class="font-medium hover:underline"
                                            >{{ post.mod.name }}</Link
                                        >
                                        <Stars
                                            v-if="post.rating !== null"
                                            :rating="post.rating"
                                        />
                                        <time
                                            :datetime="post.created_at"
                                            :class="['text-xs', muted]"
                                            >{{
                                                formatDate(post.created_at)
                                            }}</time
                                        >
                                    </p>
                                    <p
                                        v-if="post.body"
                                        class="mt-1.5 text-sm break-words whitespace-pre-line text-[#4a4a46] dark:text-[#c4c3be]"
                                    >
                                        {{ post.body }}
                                    </p>
                                </li>
                            </ul>
                            <p v-else :class="empty">No {{ activeTab }} yet.</p>
                        </template>
                    </section>
                </template>
            </div>
        </div>
    </AppLayout>
</template>
