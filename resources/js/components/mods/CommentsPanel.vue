<script setup lang="ts">
import { Link, useForm } from '@inertiajs/vue3';
import CommentNode from '@/components/mods/CommentNode.vue';
import { login } from '@/routes';
import { store } from '@/routes/comments';
import type { Comments } from '@/types';

const props = defineProps<{
    modId: string;
    comments: Comments;
    signedIn: boolean;
}>();

const form = useForm({ body: '' });

function post() {
    form.post(store(props.modId).url, {
        preserveScroll: true,
        onSuccess: () => form.reset(),
    });
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <div class="flex flex-col gap-6">
        <form
            v-if="signedIn"
            class="flex flex-col gap-3"
            @submit.prevent="post"
        >
            <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">Add a comment</span>
                <textarea
                    v-model="form.body"
                    rows="3"
                    maxlength="2000"
                    placeholder="Questions, tips, compatibility notes…"
                    class="w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#0a0a0a] dark:focus:border-[#EDEDEC]"
                />
                <span
                    v-if="form.errors.body"
                    class="text-xs text-[#f53003] dark:text-[#FF4433]"
                    >{{ form.errors.body }}</span
                >
            </label>
            <div>
                <button
                    type="submit"
                    :disabled="form.processing || !form.body.trim()"
                    class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >
                    Post comment
                </button>
            </div>
        </form>
        <p v-else :class="['text-sm', muted]">
            <Link :href="login()" class="underline underline-offset-4"
                >Log in</Link
            >
            to join the discussion.
        </p>

        <div
            v-if="comments.items.length"
            class="divide-y divide-[#19140014] border-t border-[#19140014] pt-4 dark:divide-[#3E3E3A]/60 dark:border-[#3E3E3A]/60"
        >
            <CommentNode
                v-for="comment in comments.items"
                :key="comment.id"
                :comment="comment"
                :mod-id="modId"
                :signed-in="signedIn"
                class="py-4 first:pt-0 last:pb-0"
            />
        </div>
        <p v-else :class="['text-sm', muted]">No comments yet.</p>
    </div>
</template>
