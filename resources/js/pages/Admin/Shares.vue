<script setup lang="ts">
import { Head, Link, router, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import AdminNav from '@/components/admin/AdminNav.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { formatDate } from '@/lib/format';
import { t } from '@/lib/i18n';
import { mirror, propose, purge } from '@/routes/admin/shares';
import jobs from '@/routes/admin/shares/jobs';

defineProps<{
    failedJobs: {
        id: string;
        name: string;
        failed_at: string;
        error: string;
    }[];
    mirroring: boolean;
    waitingMirrors: {
        id: number;
        name: string;
        version: string;
        url: string;
        created_at: string;
    }[];
    listings: {
        id: number;
        listing_id: string;
        type: 'vehicle' | 'save';
        name: string;
        owner: string | null;
        url: string;
        releases: number;
        pull_request_url: string | null;
        pull_request_state: 'open' | 'merged' | 'closed' | null;
    }[];
}>();

const page = usePage();

function confirmPurge(id: number, name: string) {
    if (
        confirm(
            t(
                'Delete :name for good, with its releases, files and pull request? Only possible while it is not in the index.',
                { name },
            ),
        )
    ) {
        router.delete(purge(id).url, { preserveScroll: true });
    }
}

const states = computed(() => ({
    open: t('Pull request open'),
    merged: t('In the index'),
    closed: t('Pull request closed'),
}));

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-5 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
const action = 'text-xs underline underline-offset-4 hover:no-underline';
const danger = 'text-xs text-[#f53003] hover:underline dark:text-[#FF4433]';
</script>

<template>
    <Head :title="$t('Share host')" />

    <AppLayout>
        <AdminNav />

        <p
            v-if="page.flash.status"
            role="status"
            class="mb-4 rounded-md bg-accent-wash p-3 text-sm text-accent"
        >
            {{ page.flash.status }}
        </p>
        <p
            v-if="page.props.errors.shares"
            role="alert"
            class="mb-4 rounded-md bg-[#fff2f2] p-3 text-sm text-[#f53003] dark:bg-[#1D0002] dark:text-[#FF4433]"
        >
            {{ page.props.errors.shares }}
        </p>

        <div class="flex flex-col gap-6">
            <section :class="card">
                <h2 class="font-semibold">
                    {{ $t('Failed jobs') }}
                    <span :class="['ml-1 text-sm font-normal', muted]">{{
                        failedJobs.length
                    }}</span>
                </h2>
                <ul
                    v-if="failedJobs.length"
                    class="mt-3 divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                >
                    <li
                        v-for="job in failedJobs"
                        :key="job.id"
                        class="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 py-3 text-sm"
                    >
                        <div class="min-w-0 flex-1">
                            <p class="font-medium">
                                {{ job.name }}
                                <span :class="['text-xs font-normal', muted]">{{
                                    formatDate(job.failed_at)
                                }}</span>
                            </p>
                            <p
                                :class="[
                                    'mt-0.5 font-mono text-xs break-words',
                                    muted,
                                ]"
                            >
                                {{ job.error }}
                            </p>
                        </div>
                        <span class="flex gap-3">
                            <Link
                                :href="jobs.retry(job.id)"
                                as="button"
                                preserve-scroll
                                :class="action"
                                >{{ $t('Retry') }}</Link
                            >
                            <Link
                                :href="jobs.forget(job.id)"
                                as="button"
                                preserve-scroll
                                :class="danger"
                                >{{ $t('Forget') }}</Link
                            >
                        </span>
                    </li>
                </ul>
                <p v-else :class="['mt-2 text-sm', muted]">
                    {{ $t('None.') }}
                </p>
            </section>

            <section :class="card">
                <h2 class="font-semibold">
                    {{ $t('Waiting for their archive.org copy') }}
                </h2>
                <p v-if="!mirroring" :class="['mt-2 text-sm', muted]">
                    {{
                        $t(
                            'Mirroring is off until :access and :secret are set.',
                            {
                                access: 'IA_ACCESS_KEY',
                                secret: 'IA_SECRET_KEY',
                            },
                        )
                    }}
                </p>
                <ul
                    v-else-if="waitingMirrors.length"
                    class="mt-3 divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                >
                    <li
                        v-for="release in waitingMirrors"
                        :key="release.id"
                        class="flex flex-wrap items-center justify-between gap-x-4 py-2.5 text-sm"
                    >
                        <span>
                            <a
                                :href="release.url"
                                class="font-medium hover:underline"
                                >{{ release.name }}</a
                            >
                            {{ release.version }}
                            <span :class="['text-xs', muted]">{{
                                $t('published :date', {
                                    date: formatDate(release.created_at),
                                })
                            }}</span>
                        </span>
                        <Link
                            :href="mirror(release.id)"
                            as="button"
                            preserve-scroll
                            :class="action"
                            >{{ $t('Mirror again') }}</Link
                        >
                    </li>
                </ul>
                <p v-else :class="['mt-2 text-sm', muted]">
                    {{ $t('Every release has its copy.') }}
                </p>
            </section>

            <section :class="card">
                <h2 class="font-semibold">{{ $t('Listings') }}</h2>
                <ul
                    v-if="listings.length"
                    class="mt-3 divide-y divide-[#19140014] dark:divide-[#3E3E3A]/60"
                >
                    <li
                        v-for="listing in listings"
                        :key="listing.id"
                        class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 py-2.5 text-sm"
                    >
                        <span class="min-w-0">
                            <a
                                :href="listing.url"
                                class="font-medium hover:underline"
                                >{{ listing.name }}</a
                            >
                            <span :class="['ml-2 text-xs', muted]"
                                >{{
                                    listing.type === 'vehicle'
                                        ? $t('vehicle')
                                        : $t('save')
                                }}
                                ·
                                {{ listing.owner ?? $t('deleted account') }} ·
                                {{
                                    listing.releases === 1
                                        ? $t(':count release', {
                                              count: listing.releases,
                                          })
                                        : $t(':count releases', {
                                              count: listing.releases,
                                          })
                                }}</span
                            >
                        </span>
                        <span class="flex flex-wrap items-center gap-3 text-xs">
                            <a
                                v-if="listing.pull_request_url"
                                :href="listing.pull_request_url"
                                target="_blank"
                                rel="noopener noreferrer"
                                class="underline underline-offset-4"
                                >{{
                                    listing.pull_request_state
                                        ? states[listing.pull_request_state]
                                        : $t('Pull request')
                                }}</a
                            >
                            <span v-else :class="muted">{{
                                $t('No pull request')
                            }}</span>
                            <Link
                                v-if="listing.pull_request_state !== 'merged'"
                                :href="propose(listing.id)"
                                as="button"
                                preserve-scroll
                                :class="action"
                                >{{ $t('Propose') }}</Link
                            >
                            <button
                                v-if="listing.pull_request_state !== 'merged'"
                                type="button"
                                :class="danger"
                                @click="confirmPurge(listing.id, listing.name)"
                            >
                                {{ $t('Delete') }}
                            </button>
                        </span>
                    </li>
                </ul>
                <p v-else :class="['mt-2 text-sm', muted]">
                    {{ $t('None yet.') }}
                </p>
            </section>
        </div>
    </AppLayout>
</template>
