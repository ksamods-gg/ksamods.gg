<script setup lang="ts">
import { router, useForm } from '@inertiajs/vue3';
import { ref } from 'vue';
import ModIcon from '@/components/ModIcon.vue';
import { formatDate } from '@/lib/format';
import { store as reportStore } from '@/routes/reports';
import type { Post } from '@/types';

const props = defineProps<{
    post: Post;
    deleteUrl: string;
    noun: 'review' | 'comment';
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

// Reporting opens a short form under the post; once sent, it thanks the reporter.
const reporting = ref(false);
const reported = ref(false);
const report = useForm({
    type: props.noun,
    id: props.post.id,
    reason: 'spam',
    details: '',
});

function sendReport() {
    report.post(reportStore().url, {
        preserveScroll: true,
        onSuccess: () => {
            reporting.value = false;
            reported.value = true;
        },
    });
}

const reasons = {
    spam: 'Spam',
    abuse: 'Harassment or abuse',
    'off-topic': 'Off-topic',
    other: 'Something else',
};

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const control =
    'rounded-md border border-[#19140035] bg-white px-2 py-1.5 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#161615] dark:focus:border-[#EDEDEC]';
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
                        v-if="post.canReport && !reported"
                        type="button"
                        :class="['hover:underline', muted]"
                        :aria-expanded="reporting"
                        @click="reporting = !reporting"
                    >
                        Report
                    </button>
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

            <form
                v-if="reporting"
                class="mt-2 flex flex-col gap-2 rounded-md bg-[#f4f3ef] p-3 text-sm dark:bg-[#232321]"
                @submit.prevent="sendReport"
            >
                <label class="flex flex-wrap items-center gap-2">
                    <span class="font-medium">Report this {{ noun }} for</span>
                    <select v-model="report.reason" :class="control">
                        <option
                            v-for="(label, value) in reasons"
                            :key="value"
                            :value="value"
                        >
                            {{ label }}
                        </option>
                    </select>
                </label>
                <label class="flex flex-col gap-1">
                    <span :class="muted"
                        >Anything moderators should know? (optional)</span
                    >
                    <input
                        v-model="report.details"
                        maxlength="500"
                        :class="control"
                    />
                </label>
                <p
                    v-if="report.hasErrors"
                    role="alert"
                    class="text-xs text-[#f53003] dark:text-[#FF4433]"
                >
                    The report could not be sent. Please try again.
                </p>
                <div class="flex gap-3">
                    <button
                        type="submit"
                        :disabled="report.processing"
                        class="rounded-md bg-[#1b1b18] px-3 py-1.5 font-medium text-white hover:bg-black disabled:opacity-60 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                    >
                        Send report
                    </button>
                    <button
                        type="button"
                        :class="['hover:underline', muted]"
                        @click="reporting = false"
                    >
                        Cancel
                    </button>
                </div>
            </form>
            <p v-if="reported" role="status" :class="['mt-1 text-xs', muted]">
                Thanks for the report. A moderator will take a look.
            </p>
        </div>
    </article>
</template>
