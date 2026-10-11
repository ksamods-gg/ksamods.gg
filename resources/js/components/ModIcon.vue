<script setup lang="ts">
import { cn } from '@/lib/utils';

defineProps<{ name: string; src: string | null; class?: string }>();
</script>

<template>
    <img
        v-if="src"
        :src="src"
        alt=""
        loading="lazy"
        :class="cn('shrink-0 rounded-lg object-cover', $props.class)"
    />
    <div
        v-else
        aria-hidden="true"
        :class="
            cn(
                'flex shrink-0 items-center justify-center rounded-lg bg-[#f4f3ef] font-semibold text-[#706f6c] dark:bg-[#232321] dark:text-[#A1A09A]',
                $props.class,
            )
        "
    >
        {{ name.charAt(0) }}
    </div>
</template>
