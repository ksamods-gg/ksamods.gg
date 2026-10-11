<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import { ref } from 'vue';
import AdminNav from '@/components/admin/AdminNav.vue';
import Pager from '@/components/admin/Pager.vue';
import PostSummary from '@/components/admin/PostSummary.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { posts as postsPage, reports } from '@/routes/admin';
import type { AdminPost, Paginated } from '@/types';

const props = defineProps<{
    type: 'reviews' | 'comments';
    search: string;
    posts: Paginated<AdminPost & { openReports: number }>;
}>();

const query = ref(props.search);

function apply() {
    router.get(
        postsPage(props.type).url,
        { q: query.value || undefined },
        { preserveState: true },
    );
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const card =
    'rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]';
</script>

<template>
    <Head :title="type === 'reviews' ? 'Reviews' : 'Comments'" />

    <AppLayout>
        <AdminNav />

        <form role="search" class="mb-4" @submit.prevent="apply">
            <label>
                <span class="sr-only">Search {{ type }}</span>
                <input
                    v-model="query"
                    type="search"
                    :placeholder="`Search ${type} by text`"
                    class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] sm:max-w-md dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]"
                />
            </label>
        </form>

        <ul v-if="posts.data.length" class="flex flex-col gap-3">
            <li
                v-for="post in posts.data"
                :key="`${post.type}-${post.id}`"
                :class="card"
            >
                <PostSummary :post="post">
                    <a
                        v-if="post.openReports"
                        :href="reports().url"
                        class="font-medium text-accent hover:underline"
                        >{{ post.openReports }} open
                        {{ post.openReports === 1 ? 'report' : 'reports' }}</a
                    >
                </PostSummary>
            </li>
        </ul>
        <p
            v-else
            :class="[
                'rounded-lg border border-dashed border-[#19140035] p-10 text-center text-sm dark:border-[#3E3E3A]',
                muted,
            ]"
        >
            {{ search ? `Nothing matches “${search}”.` : `No ${type} yet.` }}
        </p>

        <Pager :page="posts" />
    </AppLayout>
</template>
