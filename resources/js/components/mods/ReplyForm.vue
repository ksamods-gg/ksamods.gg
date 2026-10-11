<script setup lang="ts">
import { useForm } from '@inertiajs/vue3';
import { onMounted, useTemplateRef } from 'vue';
import { store } from '@/routes/comments';

const props = defineProps<{
    modId: string;
    // What the reply answers: a comment, or a review.
    target: { parent_id: number } | { review_id: number };
}>();

const emit = defineEmits<{ close: [] }>();

const form = useForm({ body: '' });
const textarea = useTemplateRef<HTMLTextAreaElement>('textarea');

// Opened by a Reply button, so the cursor goes straight to the text.
onMounted(() => textarea.value?.focus());

function submit() {
    form.transform((data) => ({ ...data, ...props.target })).post(
        store(props.modId).url,
        { preserveScroll: true, onSuccess: () => emit('close') },
    );
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <form
        class="flex flex-col gap-2"
        @submit.prevent="submit"
        @keydown.escape="emit('close')"
    >
        <label>
            <span class="sr-only">Your reply</span>
            <textarea
                ref="textarea"
                v-model="form.body"
                rows="2"
                maxlength="2000"
                placeholder="Write a reply…"
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
                :disabled="form.processing || !form.body.trim()"
                class="rounded-md bg-[#1b1b18] px-3 py-1.5 font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
            >
                Reply
            </button>
            <button
                type="button"
                :class="['hover:underline', muted]"
                @click="emit('close')"
            >
                Cancel
            </button>
        </div>
    </form>
</template>
