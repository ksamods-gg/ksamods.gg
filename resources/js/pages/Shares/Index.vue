<script setup lang="ts">
import { Link, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import Pager from '@/components/admin/Pager.vue';
import PageMeta from '@/components/PageMeta.vue';
import ShareRow from '@/components/ShareRow.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { create, index } from '@/routes/shares';
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
const control =
    'rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
</script>

<template>
    <PageMeta
        :title="
            types === 'vehicles'
                ? 'Kitten Space Agency vehicles'
                : 'Kitten Space Agency save games'
        "
        :description="
            types === 'vehicles'
                ? 'Rovers, rockets and landers shared by Kitten Space Agency players, with the mods each one needs.'
                : 'Save games shared by Kitten Space Agency players, with the mods each one needs.'
        "
    />

    <AppLayout>
        <div class="flex flex-wrap items-end justify-between gap-4">
            <div>
                <h1 class="text-2xl font-semibold tracking-tight">
                    {{ types === 'vehicles' ? 'Vehicles' : 'Save games' }} for
                    Kitten Space Agency
                </h1>
                <p :class="['mt-1 text-sm', muted]">
                    {{ shares.total }} shared by players, with the mods each one
                    needs. Your mod manager installs those mods first.
                </p>
            </div>
            <Link
                :href="create()"
                class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >Share yours</Link
            >
        </div>

        <p
            v-if="$page.flash.status"
            role="status"
            class="mt-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ $page.flash.status }}
        </p>

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

        <form role="search" class="mt-4" @submit.prevent="apply">
            <label class="block">
                <span class="sr-only">Search {{ types }}</span>
                <input
                    v-model="query"
                    type="search"
                    :placeholder="`Search ${types} by name`"
                    :class="[control, 'w-full py-2.5']"
                />
            </label>
        </form>
        <p :class="['mt-3 text-sm', muted]" aria-live="polite">
            {{ shares.total }}
            {{
                shares.total === 1
                    ? types === 'vehicles'
                        ? 'vehicle'
                        : 'save'
                    : types
            }}
        </p>

        <ul v-if="shares.data.length" class="mt-4 flex flex-col gap-3">
            <li v-for="share in shares.data" :key="share.id">
                <ShareRow :share="share" />
            </li>
        </ul>
        <p
            v-else
            :class="[
                'mt-4 rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
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
