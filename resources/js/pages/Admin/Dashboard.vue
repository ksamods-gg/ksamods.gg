<script setup lang="ts">
import { Head, Link, usePage } from '@inertiajs/vue3';
import AdminNav from '@/components/admin/AdminNav.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatNumber } from '@/lib/format';
import { refreshIndex, reports, users } from '@/routes/admin';

defineProps<{
    stats: {
        users: number;
        newUsers: number;
        reviews: number;
        comments: number;
        openReports: number;
        mods: number;
    };
}>();

const page = usePage();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <Head title="Admin" />

    <AppLayout>
        <AdminNav />

        <dl class="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div :class="card">
                <dt :class="['text-sm', muted]">Open reports</dt>
                <dd class="mt-1 text-2xl font-semibold tabular-nums">
                    <Link :href="reports()" class="hover:underline">{{
                        formatNumber(stats.openReports)
                    }}</Link>
                </dd>
            </div>
            <div :class="card">
                <dt :class="['text-sm', muted]">Users</dt>
                <dd class="mt-1 text-2xl font-semibold tabular-nums">
                    <Link :href="users()" class="hover:underline">{{
                        formatNumber(stats.users)
                    }}</Link>
                </dd>
                <dd :class="['text-xs', muted]">
                    {{ formatNumber(stats.newUsers) }} joined this week
                </dd>
            </div>
            <div :class="card">
                <dt :class="['text-sm', muted]">Mods in the index</dt>
                <dd class="mt-1 text-2xl font-semibold tabular-nums">
                    {{ formatNumber(stats.mods) }}
                </dd>
            </div>
            <div :class="card">
                <dt :class="['text-sm', muted]">Reviews</dt>
                <dd class="mt-1 text-2xl font-semibold tabular-nums">
                    {{ formatNumber(stats.reviews) }}
                </dd>
            </div>
            <div :class="card">
                <dt :class="['text-sm', muted]">Comments</dt>
                <dd class="mt-1 text-2xl font-semibold tabular-nums">
                    {{ formatNumber(stats.comments) }}
                </dd>
            </div>
        </dl>

        <section :class="[card, 'mt-6 flex flex-col gap-3 p-5']">
            <div>
                <h2 class="font-semibold">Content index</h2>
                <p :class="['mt-1 text-sm', muted]">
                    The site rechecks the KSA content index every 10 minutes.
                    Refresh now to show a newly merged listing right away.
                </p>
            </div>
            <p
                v-if="page.flash.status"
                role="status"
                class="text-sm text-accent"
            >
                {{ page.flash.status }}
            </p>
            <p
                v-if="page.props.errors.index"
                role="alert"
                class="text-sm text-[#f53003] dark:text-[#FF4433]"
            >
                {{ page.props.errors.index }}
            </p>
            <div>
                <Link
                    :href="refreshIndex()"
                    as="button"
                    preserve-scroll
                    class="rounded-md border border-[#19140035] px-4 py-2 text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]"
                >
                    Refresh content index
                </Link>
            </div>
        </section>
    </AppLayout>
</template>
