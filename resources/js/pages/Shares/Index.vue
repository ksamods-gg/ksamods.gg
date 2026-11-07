<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import Pager from '@/components/admin/Pager.vue';
import BrowseTabs from '@/components/BrowseTabs.vue';
import PageMeta from '@/components/PageMeta.vue';
import ShareRow from '@/components/ShareRow.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { shares as sharesFeed } from '@/routes/feeds';
import { create, index } from '@/routes/shares';
import type { Paginated, ShareSummary } from '@/types';

const props = defineProps<{
    types: 'vehicles' | 'saves';
    search: string;
    sort: 'updated' | 'downloads';
    shares: Paginated<ShareSummary>;
}>();

const query = ref(props.search);
const sort = ref(props.sort);

function apply() {
    router.get(
        index(props.types).url,
        {
            q: query.value || undefined,
            sort: sort.value === 'downloads' ? 'downloads' : undefined,
        },
        { preserveState: true },
    );
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const control =
    'rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
</script>

<template>
    <PageMeta
        :title="
            types === 'vehicles'
                ? $t('Kitten Space Agency vehicles')
                : $t('Kitten Space Agency save games')
        "
        :description="
            types === 'vehicles'
                ? $t(
                      'Rovers, rockets and landers shared by Kitten Space Agency players, with the mods each one needs.',
                  )
                : $t(
                      'Save games shared by Kitten Space Agency players, with the mods each one needs.',
                  )
        "
    />
    <Head>
        <link
            rel="alternate"
            type="application/atom+xml"
            :title="$t('New vehicles and saves')"
            :href="sharesFeed('atom').url"
        />
    </Head>

    <AppLayout>
        <BrowseTabs :current="types" />

        <div class="flex flex-wrap items-end justify-between gap-4">
            <div>
                <h1 class="text-2xl font-semibold tracking-tight">
                    {{
                        types === 'vehicles'
                            ? $t('Vehicles for Kitten Space Agency')
                            : $t('Save games for Kitten Space Agency')
                    }}
                </h1>
                <p :class="['mt-1 text-sm', muted]">
                    {{
                        $t(
                            ':count shared by players, with the mods each one needs. Your mod manager installs those mods first.',
                            { count: shares.total },
                        )
                    }}
                    <a
                        :href="sharesFeed('atom').url"
                        class="underline underline-offset-4"
                        >{{ $t('New releases feed') }}</a
                    >
                </p>
            </div>
            <Link
                :href="create()"
                class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >{{ $t('Share yours') }}</Link
            >
        </div>

        <p
            v-if="$page.flash.status"
            role="status"
            class="mt-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ $page.flash.status }}
        </p>

        <form role="search" class="mt-6" @submit.prevent="apply">
            <label class="block">
                <span class="sr-only">{{
                    types === 'vehicles'
                        ? $t('Search vehicles')
                        : $t('Search saves')
                }}</span>
                <input
                    v-model="query"
                    type="search"
                    :placeholder="
                        types === 'vehicles'
                            ? $t('Search vehicles by name')
                            : $t('Search saves by name')
                    "
                    :class="[control, 'w-full py-2.5']"
                />
            </label>
            <label class="mt-3 flex items-center gap-2 text-sm">
                <span :class="muted">{{ $t('Sort by') }}</span>
                <select
                    v-model="sort"
                    :class="[control, 'py-1.5']"
                    @change="apply"
                >
                    <option value="updated">
                        {{ $t('Recently updated') }}
                    </option>
                    <option value="downloads">
                        {{ $t('Most downloaded') }}
                    </option>
                </select>
            </label>
        </form>
        <p :class="['mt-3 text-sm', muted]" aria-live="polite">
            {{
                types === 'vehicles'
                    ? shares.total === 1
                        ? $t(':count vehicle', { count: shares.total })
                        : $t(':count vehicles', { count: shares.total })
                    : shares.total === 1
                      ? $t(':count save', { count: shares.total })
                      : $t(':count saves', { count: shares.total })
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
            <template v-if="search">{{
                $t('Nothing matches “:search”.', { search })
            }}</template>
            <template v-else>
                {{
                    types === 'vehicles'
                        ? $t('No vehicles yet.')
                        : $t('No saves yet.')
                }}
                <Link :href="create()" class="underline underline-offset-4">{{
                    $t('Share the first one')
                }}</Link
                >.
            </template>
        </p>

        <Pager :page="shares" />
    </AppLayout>
</template>
