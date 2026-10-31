<script setup lang="ts">
import { router } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import Stars from '@/components/Stars.vue';
import { formatDate } from '@/lib/format';
import { t } from '@/lib/i18n';
import { destroy as destroyComment } from '@/routes/comments';
import { destroy as destroyReview } from '@/routes/reviews';
import type { AdminPost } from '@/types';

/**
 * A review or comment in the admin panel: who wrote it where, what it says, and a delete button.
 */
const props = defineProps<{ post: AdminPost }>();

function remove() {
    const author = props.post.author.name;
    const message =
        props.post.type === 'review'
            ? t('Delete this review by :author? This cannot be undone.', {
                  author,
              })
            : t('Delete this comment by :author? This cannot be undone.', {
                  author,
              });

    if (confirm(message)) {
        router.delete(
            props.post.type === 'review'
                ? destroyReview(props.post.id).url
                : destroyComment(props.post.id).url,
            { preserveScroll: true },
        );
    }
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <div class="flex gap-3">
        <ModIcon
            :name="post.author.name"
            :src="post.author.avatar"
            class="size-8 shrink-0 rounded-full text-sm"
        />
        <div class="min-w-0 flex-1">
            <p class="flex flex-wrap items-baseline gap-x-2 text-sm">
                <span class="font-medium">{{ post.author.name }}</span>
                <span :class="muted">
                    {{
                        post.type === 'review'
                            ? $t('reviewed :mod', { mod: post.mod.name })
                            : $t('commented on :mod', { mod: post.mod.name })
                    }}
                </span>
                <time :datetime="post.created_at" :class="['text-xs', muted]">{{
                    formatDate(post.created_at)
                }}</time>
            </p>
            <Stars v-if="post.rating" :rating="post.rating" class="mt-1" />
            <p
                v-if="post.body"
                class="mt-1 text-sm break-words whitespace-pre-line"
            >
                {{ post.body }}
            </p>
            <p v-else :class="['mt-1 text-sm italic', muted]">
                {{ $t('No text.') }}
            </p>
            <div class="mt-2 flex flex-wrap gap-4 text-xs">
                <!-- A plain link, so the browser keeps the #anchor. -->
                <a :href="post.url" :class="['hover:underline', muted]">{{
                    $t('View on the mod page')
                }}</a>
                <slot />
                <button
                    type="button"
                    class="text-[#f53003] hover:underline dark:text-[#FF4433]"
                    @click="remove"
                >
                    {{
                        post.type === 'review'
                            ? $t('Delete review')
                            : $t('Delete comment')
                    }}
                </button>
            </div>
        </div>
    </div>
</template>
