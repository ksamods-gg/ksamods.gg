<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { formatNumber, tagName } from '@/lib/format';
import { show } from '@/routes/mods';
import type { ModSummary } from '@/types';

/**
 * One mod in the mod list: icon, name, summary and tags, with its numbers on the right.
 */
defineProps<{ mod: ModSummary; tagNames: Record<string, string> }>();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const chip =
    'inline-flex items-center gap-1 rounded-full bg-[#f4f3ef] px-2 py-0.5 text-xs dark:bg-[#232321]';
</script>

<template>
    <article
        class="relative grid grid-cols-[3.5rem_minmax(0,1fr)] gap-x-4 gap-y-3 rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] sm:grid-cols-[5rem_minmax(0,1fr)_auto] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
    >
        <ModIcon
            :name="mod.name"
            :src="mod.icon"
            class="size-14 rounded-xl text-xl sm:row-span-2 sm:size-20 sm:text-2xl"
        />

        <div class="min-w-0">
            <h2 class="flex flex-wrap items-baseline gap-x-2">
                <!-- The whole row opens the mod; the name is the link. -->
                <Link
                    :href="show(mod.id)"
                    class="text-base font-semibold after:absolute after:inset-0 after:rounded-lg hover:underline"
                    >{{ mod.name }}</Link
                >
                <span :class="['text-sm font-normal', muted]"
                    >by {{ mod.authors.join(', ') }}</span
                >
            </h2>
            <p
                class="mt-1 line-clamp-2 text-sm text-[#4a4a46] dark:text-[#c4c3be]"
            >
                {{ mod.abstract }}
            </p>
        </div>

        <!-- The numbers: downloads, rating and when it last changed. -->
        <dl
            :class="[
                'col-span-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm sm:col-span-1 sm:row-span-2 sm:flex-col sm:items-end sm:justify-start sm:text-right',
                muted,
            ]"
        >
            <div class="flex items-center gap-1.5">
                <dt class="sr-only">Downloads</dt>
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
                    <path
                        d="M8 2.5v8m0 0L4.75 7.25M8 10.5l3.25-3.25M3 13.5h10"
                    />
                </svg>
                <dd
                    class="font-medium text-[#1b1b18] tabular-nums dark:text-[#EDEDEC]"
                >
                    {{ formatNumber(mod.downloads) }}
                </dd>
            </div>
            <div v-if="mod.rating" class="flex items-center gap-1.5">
                <dt class="sr-only">Rating</dt>
                <svg
                    viewBox="0 0 20 20"
                    aria-hidden="true"
                    class="size-3.5 fill-[#f5a623]"
                >
                    <path
                        d="M10 1.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L10 14.8l-5.2 2.8 1-5.8L1.5 7.7l5.9-.9L10 1.5z"
                    />
                </svg>
                <dd class="tabular-nums">
                    <span class="text-[#1b1b18] dark:text-[#EDEDEC]">{{
                        mod.rating.average
                    }}</span>
                    ({{ mod.rating.count }})
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
                    <time :datetime="mod.updated_at">{{
                        mod.updated_ago
                    }}</time>
                </dd>
            </div>
        </dl>

        <ul
            class="col-span-2 flex flex-wrap gap-1.5 sm:col-span-1 sm:col-start-2"
            aria-label="Tags"
        >
            <li v-if="mod.type === 'mod-loader'" :class="[chip, 'text-accent']">
                Mod loader
            </li>
            <li v-if="mod.loader" :class="chip">
                {{ mod.loader }}
            </li>
            <li v-for="tag in mod.tags" :key="tag" :class="chip">
                {{ tagName(tag, tagNames) }}
            </li>
            <li v-if="mod.version" :class="[chip, 'font-mono tabular-nums']">
                {{ mod.version }}
            </li>
        </ul>
    </article>
</template>
