<script setup lang="ts">
import { Link, router, usePage } from '@inertiajs/vue3';
import { login } from '@/routes';
import { toggle } from '@/routes/follows';

/**
 * Follows a mod, vehicle or save for its new releases, which then show up in the bell (and the
 * email digest, for players who turned it on). Guests are sent to log in first.
 */
const props = defineProps<{
    kind: 'mod' | 'vehicle' | 'save';
    id: string;
    following: boolean;
}>();

const page = usePage();

function follow() {
    router.post(
        toggle().url,
        { kind: props.kind, id: props.id },
        { preserveScroll: true, only: ['following'] },
    );
}

const button =
    'inline-flex items-center gap-1.5 rounded-md border border-[#19140035] px-3 py-1.5 text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
</script>

<template>
    <button
        v-if="page.props.auth.user"
        type="button"
        :aria-pressed="following"
        :class="[button, following && 'bg-[#f4f3ef] dark:bg-[#232321]']"
        @click="follow"
    >
        <svg
            viewBox="0 0 24 24"
            :fill="following ? 'currentColor' : 'none'"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            class="size-4"
        >
            <path
                d="M6 8a6 6 0 1 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10.3 21a1.94 1.94 0 0 0 3.4 0"
            />
        </svg>
        {{ following ? 'Following' : 'Follow' }}
    </button>
    <Link v-else :href="login()" :class="button"> Follow </Link>
</template>
