<script setup lang="ts">
import { useForm } from '@inertiajs/vue3';
import { ref } from 'vue';
import SocialPost from '@/components/SocialPost.vue';
import { destroy, update } from '@/routes/comments';
import type { Post } from '@/types';

const props = defineProps<{
    comment: Post;
    signedIn: boolean;
    compact?: boolean;
}>();

defineEmits<{ reply: [] }>();

// The author's own comment, edited in place.
const editing = ref(false);
const form = useForm({ body: '' });

function startEditing() {
    form.body = props.comment.body ?? '';
    form.clearErrors();
    editing.value = true;
}

function save() {
    form.patch(update(props.comment.id).url, {
        preserveScroll: true,
        onSuccess: () => (editing.value = false),
    });
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <SocialPost
        :post="comment"
        :delete-url="destroy(comment.id).url"
        noun="comment"
        :compact="compact"
        :anchor="`comment-${comment.id}`"
    >
        <template #actions>
            <button
                v-if="signedIn && !comment.deleted && !editing"
                type="button"
                :class="['hover:underline', muted]"
                @click="$emit('reply')"
            >
                Reply
            </button>
            <button
                v-if="comment.mine && !editing"
                type="button"
                :class="['hover:underline', muted]"
                @click="startEditing"
            >
                Edit
            </button>
        </template>

        <form
            v-if="editing"
            class="mt-2 flex flex-col gap-2"
            @submit.prevent="save"
        >
            <label>
                <span class="sr-only">Edit your comment</span>
                <textarea
                    v-model="form.body"
                    rows="3"
                    maxlength="2000"
                    class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#0a0a0a] dark:focus:border-[#EDEDEC]"
                />
            </label>
            <span
                v-if="form.errors.body"
                class="text-xs text-[#f53003] dark:text-[#FF4433]"
                >{{ form.errors.body }}</span
            >
            <div class="flex gap-3 text-sm">
                <button
                    type="submit"
                    :disabled="form.processing"
                    class="rounded-md bg-[#1b1b18] px-4 py-2 font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >
                    Save
                </button>
                <button
                    type="button"
                    :class="['hover:underline', muted]"
                    @click="editing = false"
                >
                    Cancel
                </button>
            </div>
        </form>
        <p v-else-if="comment.deleted" :class="['mt-1 text-sm italic', muted]">
            This comment was deleted.
        </p>
        <p v-else class="mt-1 text-sm break-words whitespace-pre-line">
            {{ comment.body }}
        </p>
    </SocialPost>
</template>
