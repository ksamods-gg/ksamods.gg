<script setup lang="ts">
import { router } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { formatDate } from '@/lib/format';
import type { Post } from '@/types';

const props = defineProps<{
    post: Post;
    deleteUrl: string;
    noun: string;
    // Smaller, for replies.
    compact?: boolean;
    // The element id a link can point at, such as a notification's #comment-12.
    anchor?: string;
}>();

function remove() {
    if (confirm(`Delete this ${props.noun}?`)) {
        router.delete(props.deleteUrl, { preserveScroll: true });
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <!-- No vertical spacing of its own: the list it sits in spaces it. A linked-to post is highlighted. -->
    <article
        :id="anchor"
        class="-m-2 flex scroll-mt-24 gap-3 rounded-md p-2 target:bg-[#f5a623]/10 target:ring-1 target:ring-[#f5a623]/40"
    >
        <ModIcon
            :name="post.author?.name ?? '–'"
            :src="post.author?.avatar ?? null"
            :class="
                compact
                    ? 'size-6 rounded-full text-xs'
                    : 'size-8 rounded-full text-sm'
            "
        />
        <div class="min-w-0 flex-1">
            <header
                class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-sm"
            >
                <span v-if="post.author" class="font-medium">{{
                    post.author.name
                }}</span>
                <span v-else :class="['italic', muted]">Deleted</span>
                <span v-if="post.mine" :class="['text-xs', muted]">(you)</span>
                <time :datetime="post.created_at" :class="['text-xs', muted]">
                    {{ formatDate(post.created_at)
                    }}<template v-if="post.edited"> · edited</template>
                </time>
                <span class="ml-auto flex gap-3 text-xs">
                    <slot name="actions" />
                    <button
                        v-if="post.canDelete"
                        type="button"
                        :class="['hover:underline', muted]"
                        @click="remove"
                    >
                        Delete
                    </button>
                </span>
            </header>
            <slot />
        </div>
    </article>
</template>
