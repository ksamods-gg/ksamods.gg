<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import Pager from '@/components/admin/Pager.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { create, index, show } from '@/routes/shares';
import type { Paginated, ShareSummary } from '@/types';

const props = defineProps<{
    types: 'vehicles' | 'saves';
    search: string;
    shares: Paginated<ShareSummary>;
}>();

const query = ref(props.search);

function apply() {
    router.get(
        index(props.types).url,
        { q: query.value || undefined },
        { preserveState: true },
    );
}

const tabs = [
    { types: 'vehicles', label: 'Vehicles' },
    { types: 'saves', label: 'Saves' },
] as const;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'relative flex flex-col rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]';
</script>

<template>
    <Head :title="types === 'vehicles' ? 'Vehicles' : 'Saves'" />

    <AppLayout>
        <div class="flex flex-wrap items-end justify-between gap-4">
            <div>
                <h1 class="text-2xl font-semibold tracking-tight">
                    Vehicles and saves
                </h1>
                <p :class="['mt-1 text-sm', muted]">
                    Shared by players, with the mods each one needs. Your client
                    installs those mods first.
                </p>
            </div>
            <Link
                :href="create()"
                class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >Share yours</Link
            >
        </div>

        <nav
            class="mt-6 flex gap-6 text-sm shadow-[inset_0_-1px_0_#19140014] dark:shadow-[inset_0_-1px_0_#3E3E3A]"
            aria-label="Type"
        >
            <Link
                v-for="tab in tabs"
                :key="tab.types"
                :href="index(tab.types)"
                :aria-current="tab.types === types ? 'page' : undefined"
                :class="[
                    'border-b-2 pb-2 font-medium',
                    tab.types === types
                        ? 'border-[#1b1b18] dark:border-[#EDEDEC]'
                        : [
                              'border-transparent hover:text-[#1b1b18] dark:hover:text-[#EDEDEC]',
                              muted,
                          ],
                ]"
                >{{ tab.label }}</Link
            >
        </nav>

        <form role="search" class="mt-4 mb-6" @submit.prevent="apply">
            <label>
                <span class="sr-only">Search {{ types }}</span>
                <input
                    v-model="query"
                    type="search"
                    :placeholder="`Search ${types} by name`"
                    class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] sm:max-w-md dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]"
                />
            </label>
        </form>

        <ul
            v-if="shares.data.length"
            class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
        >
            <li v-for="share in shares.data" :key="share.id" :class="card">
                <Link
                    :href="show({ types, id: share.id })"
                    class="font-semibold after:absolute after:inset-0 after:rounded-lg hover:underline"
                    >{{ share.name }}</Link
                >
                <p :class="['text-xs', muted]">
                    by {{ share.authors.join(', ') }}
                </p>
                <p class="mt-2 line-clamp-3 text-sm">{{ share.abstract }}</p>
                <p
                    :class="[
                        'mt-auto flex flex-wrap gap-x-3 pt-3 text-xs',
                        muted,
                    ]"
                >
                    <span v-if="share.version" class="font-mono"
                        >v{{ share.version }}</span
                    >
                    <span v-if="share.game_version"
                        >Game {{ share.game_version }}</span
                    >
                </p>
            </li>
        </ul>
        <p
            v-else
            :class="[
                'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            <template v-if="search">Nothing matches “{{ search }}”.</template>
            <template v-else>
                No {{ types }} yet.
                <Link :href="create()" class="underline underline-offset-4"
                    >Share the first one</Link
                >.
            </template>
        </p>

        <Pager :page="shares" />
    </AppLayout>
</template>
