<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import { computed, ref } from 'vue';
import ModIcon from '@/components/ModIcon.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate, formatNumber, tagName } from '@/lib/format';
import { show } from '@/routes/mods';
import type { ModSummary } from '@/types';

const props = defineProps<{
    mods: ModSummary[];
    tagNames: Record<string, string>;
}>();

const search = ref('');
const activeTag = ref<string | null>(null);
const sort = ref<'downloads' | 'updated' | 'name'>('downloads');

const tags = computed(() =>
    [...new Set(props.mods.flatMap((mod) => mod.tags))].sort((a, b) =>
        tagName(a, props.tagNames).localeCompare(tagName(b, props.tagNames)),
    ),
);

const visibleMods = computed(() => {
    const query = search.value.trim().toLowerCase();

    return props.mods
        .filter(
            (mod) =>
                (!activeTag.value || mod.tags.includes(activeTag.value)) &&
                (!query ||
                    [mod.name, mod.abstract ?? '', ...mod.authors].some(
                        (text) => text.toLowerCase().includes(query),
                    )),
        )
        .sort((a, b) => {
            if (sort.value === 'name') {
                return a.name.localeCompare(b.name);
            }

            if (sort.value === 'updated') {
                return b.updated_at.localeCompare(a.updated_at);
            }

            return b.downloads - a.downloads;
        });
});

function resetFilters() {
    search.value = '';
    activeTag.value = null;
}

const chip =
    'rounded-full border px-3 py-1 text-xs transition-colors aria-pressed:border-[#1b1b18] aria-pressed:bg-[#1b1b18] aria-pressed:text-white dark:aria-pressed:border-[#EDEDEC] dark:aria-pressed:bg-[#EDEDEC] dark:aria-pressed:text-[#0a0a0a] border-[#19140035] hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <Head title="Mods" />

    <AppLayout>
        <div class="mb-6">
            <h1 class="text-2xl font-semibold tracking-tight">
                Mods for Kitten Space Agency
            </h1>
            <p class="mt-1 text-sm text-[#706f6c] dark:text-[#A1A09A]">
                {{ mods.length }} listings from the community content index.
            </p>
        </div>

        <div class="mb-4 flex flex-col gap-3 sm:flex-row">
            <label class="flex-1">
                <span class="sr-only">Search mods</span>
                <input
                    v-model="search"
                    type="search"
                    placeholder="Search by name, description or author"
                    class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]"
                />
            </label>
            <label class="flex items-center gap-2 text-sm">
                <span class="text-[#706f6c] dark:text-[#A1A09A]">Sort</span>
                <select
                    v-model="sort"
                    class="rounded-md border border-[#19140035] bg-white px-3 py-2 dark:border-[#3E3E3A] dark:bg-[#161615]"
                >
                    <option value="downloads">Most downloaded</option>
                    <option value="updated">Recently updated</option>
                    <option value="name">Name</option>
                </select>
            </label>
        </div>

        <div class="mb-6 flex flex-wrap gap-2" aria-label="Filter by tag">
            <button
                type="button"
                :aria-pressed="activeTag === null"
                :class="chip"
                @click="activeTag = null"
            >
                All
            </button>
            <button
                v-for="tag in tags"
                :key="tag"
                type="button"
                :aria-pressed="activeTag === tag"
                :class="chip"
                @click="activeTag = activeTag === tag ? null : tag"
            >
                {{ tagName(tag, tagNames) }}
            </button>
        </div>

        <p
            v-if="visibleMods.length === 0"
            class="rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm text-[#706f6c] dark:border-[#3E3E3A] dark:text-[#A1A09A]"
        >
            No mods match your filters.
            <button
                type="button"
                class="underline underline-offset-4"
                @click="resetFilters"
            >
                Clear filters
            </button>
        </p>

        <ul v-else class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <li v-for="mod in visibleMods" :key="mod.id">
                <Link
                    :href="show(mod.id)"
                    class="flex h-full flex-col rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
                >
                    <div class="flex gap-3">
                        <ModIcon
                            :name="mod.name"
                            :src="mod.icon"
                            class="size-12 text-lg"
                        />
                        <div class="min-w-0">
                            <h2 class="truncate font-medium">
                                {{ mod.name }}
                            </h2>
                            <p
                                class="truncate text-xs text-[#706f6c] dark:text-[#A1A09A]"
                            >
                                by {{ mod.authors.join(', ') }}
                            </p>
                        </div>
                    </div>

                    <p
                        class="mt-3 line-clamp-3 flex-1 text-sm text-[#4a4a46] dark:text-[#c4c3be]"
                    >
                        {{ mod.abstract }}
                    </p>

                    <div
                        class="mt-4 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-[#706f6c] dark:text-[#A1A09A]"
                    >
                        <span
                            v-if="mod.type === 'mod-loader'"
                            class="font-medium text-[#f53003] dark:text-[#FF4433]"
                        >
                            Mod loader
                        </span>
                        <span v-if="mod.version">v{{ mod.version }}</span>
                        <span>{{ formatNumber(mod.downloads) }} downloads</span>
                        <span>Updated {{ formatDate(mod.updated_at) }}</span>
                    </div>
                </Link>
            </li>
        </ul>
    </AppLayout>
</template>
