<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import type { Paginated } from '@/types';

/**
 * Previous and next links for a Laravel paginator, shown when there is more than one page.
 */
defineProps<{ page: Paginated<unknown> }>();
</script>

<template>
    <nav
        v-if="page.last_page > 1"
        class="mt-6 flex items-center justify-center gap-4 text-sm"
        aria-label="Pages"
    >
        <Link
            v-if="page.prev_page_url"
            :href="page.prev_page_url"
            class="hover:underline"
            >Previous</Link
        >
        <span class="text-[#706f6c] dark:text-[#A1A09A]"
            >Page {{ page.current_page }} of {{ page.last_page }}</span
        >
        <Link
            v-if="page.next_page_url"
            :href="page.next_page_url"
            class="hover:underline"
            >Next</Link
        >
    </nav>
</template>
