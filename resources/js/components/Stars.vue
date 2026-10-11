<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{ rating: number }>();

const filled = computed(() => Math.round(props.rating));
</script>

<template>
    <span
        class="inline-flex items-center"
        role="img"
        :aria-label="`${rating} out of 5 stars`"
    >
        <svg
            v-for="star in 5"
            :key="star"
            viewBox="0 0 20 20"
            aria-hidden="true"
            :class="[
                'size-4',
                star <= filled
                    ? 'fill-[#f5a623]'
                    : 'fill-[#19140026] dark:fill-[#3E3E3A]',
            ]"
        >
            <path
                d="M10 1.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L10 14.8l-5.2 2.8 1-5.8L1.5 7.7l5.9-.9L10 1.5z"
            />
        </svg>
    </span>
</template>
