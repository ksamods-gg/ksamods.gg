<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { show } from '@/routes/shares';
import type { ShareSummary } from '@/types';

/**
 * One vehicle or save in its list, laid out like a mod's row: name, summary and tags, with the
 * game build it needs and when it last changed on the right.
 */
defineProps<{ share: ShareSummary }>();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const chip =
    'inline-flex items-center gap-1 rounded-full bg-[#f4f3ef] px-2 py-0.5 text-xs dark:bg-[#232321]';
</script>

<template>
    <article
        class="relative grid grid-cols-[3.5rem_minmax(0,1fr)] gap-x-4 gap-y-3 rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] sm:grid-cols-[5rem_minmax(0,1fr)_auto] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
    >
        <ModIcon
            :name="share.name"
            :src="null"
            class="size-14 rounded-xl text-xl sm:row-span-2 sm:size-20 sm:text-2xl"
        />

        <div class="min-w-0">
            <h2 class="flex flex-wrap items-baseline gap-x-2">
                <!-- The whole row opens the listing; the name is the link. -->
                <Link
                    :href="
                        show({
                            types:
                                share.type === 'vehicle' ? 'vehicles' : 'saves',
                            id: share.id,
                        })
                    "
                    class="text-base font-semibold after:absolute after:inset-0 after:rounded-lg hover:underline"
                    >{{ share.name }}</Link
                >
                <span :class="['text-sm font-normal', muted]"
                    >by {{ share.authors.join(', ') }}</span
                >
            </h2>
            <p
                class="mt-1 line-clamp-2 text-sm text-[#4a4a46] dark:text-[#c4c3be]"
            >
                {{ share.abstract }}
            </p>
        </div>

        <dl
            :class="[
                'col-span-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm sm:col-span-1 sm:row-span-2 sm:flex-col sm:items-end sm:justify-start sm:text-right',
                muted,
            ]"
        >
            <div v-if="share.game_version" class="flex items-center gap-1.5">
                <dt class="sr-only">Game build</dt>
                <svg
                    viewBox="0 0 16 16"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.5"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    aria-hidden="true"
                    class="size-3.5"
                >
                    <rect x="2.5" y="3.5" width="11" height="9" rx="1.5" />
                    <path d="M5 7.5h2M6 6.5v2M10 7.5h1" />
                </svg>
                <dd
                    class="font-medium text-[#1b1b18] tabular-nums dark:text-[#EDEDEC]"
                >
                    {{ share.game_version }}
                </dd>
            </div>
            <div class="flex items-center gap-1.5">
                <dt class="sr-only">Updated</dt>
                <svg
                    viewBox="0 0 16 16"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.5"
                    stroke-linecap="round"
                    aria-hidden="true"
                    class="size-3.5"
                >
                    <circle cx="8" cy="8" r="5.75" />
                    <path d="M8 5v3l2 1.5" />
                </svg>
                <dd>
                    <time :datetime="share.updated_at">{{
                        share.updated_ago
                    }}</time>
                </dd>
            </div>
        </dl>

        <ul
            class="col-span-2 flex flex-wrap gap-1.5 sm:col-span-1 sm:col-start-2"
            aria-label="Tags"
        >
            <li :class="[chip, 'text-accent']">
                {{ share.type === 'vehicle' ? 'Vehicle' : 'Save' }}
            </li>
            <li v-for="tag in share.tags" :key="tag" :class="chip">
                {{ tag }}
            </li>
            <li v-if="share.version" :class="[chip, 'font-mono tabular-nums']">
                {{ share.version }}
            </li>
        </ul>
    </article>
</template>
