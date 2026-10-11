<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import AdminNav from '@/components/admin/AdminNav.vue';
import Pager from '@/components/admin/Pager.vue';
import PostSummary from '@/components/admin/PostSummary.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate } from '@/lib/format';
import { reports as reportsPage } from '@/routes/admin';
import { resolve } from '@/routes/admin/reports';
import type { AdminReport, Paginated } from '@/types';

defineProps<{
    status: 'open' | 'resolved';
    reports: Paginated<AdminReport>;
}>();

const filters = [
    { status: 'open', label: 'Open', href: reportsPage().url },
    {
        status: 'resolved',
        label: 'Resolved',
        href: reportsPage({ query: { status: 'resolved' } }).url,
    },
] as const;

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <Head title="Reports" />

    <AppLayout>
        <AdminNav />

        <div class="mb-4 flex gap-2 text-sm" role="group" aria-label="Status">
            <Link
                v-for="filter in filters"
                :key="filter.status"
                :href="filter.href"
                :aria-current="filter.status === status ? 'page' : undefined"
                :class="[
                    'rounded-full px-3 py-1',
                    filter.status === status
                        ? 'bg-[#1b1b18] text-white dark:bg-[#EDEDEC] dark:text-[#0a0a0a]'
                        : 'bg-[#f4f3ef] hover:bg-[#ebeae5] dark:bg-[#232321] dark:hover:bg-[#2c2c29]',
                ]"
                >{{ filter.label }}</Link
            >
        </div>

        <ul v-if="reports.data.length" class="flex flex-col gap-3">
            <li v-for="report in reports.data" :key="report.id" :class="card">
                <p class="flex flex-wrap items-baseline gap-x-2 text-sm">
                    <span class="font-semibold">{{ report.reason }}</span>
                    <span :class="muted"
                        >reported by {{ report.reporter }}</span
                    >
                    <time
                        :datetime="report.created_at"
                        :class="['text-xs', muted]"
                        >{{ formatDate(report.created_at) }}</time
                    >
                    <span
                        v-if="report.resolved_at"
                        :class="['ml-auto text-xs', muted]"
                        >Resolved {{ formatDate(report.resolved_at) }}</span
                    >
                </p>
                <p
                    v-if="report.details"
                    class="mt-1 text-sm break-words whitespace-pre-line"
                >
                    “{{ report.details }}”
                </p>

                <div class="mt-3 rounded-md bg-[#f4f3ef] p-3 dark:bg-[#1c1c1a]">
                    <PostSummary v-if="report.post" :post="report.post">
                        <Link
                            v-if="!report.resolved_at"
                            :href="resolve(report.id)"
                            as="button"
                            preserve-scroll
                            class="hover:underline"
                            >Dismiss, it's fine</Link
                        >
                    </PostSummary>
                    <p v-else :class="['text-sm', muted]">
                        The reported post has been deleted.
                        <Link
                            v-if="!report.resolved_at"
                            :href="resolve(report.id)"
                            as="button"
                            preserve-scroll
                            class="ml-2 text-[#1b1b18] hover:underline dark:text-[#EDEDEC]"
                            >Mark resolved</Link
                        >
                    </p>
                </div>
            </li>
        </ul>
        <p
            v-else
            :class="[
                'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            {{
                status === 'open'
                    ? 'No open reports. Nothing needs a look right now.'
                    : 'No resolved reports yet.'
            }}
        </p>

        <Pager :page="reports" />
    </AppLayout>
</template>
