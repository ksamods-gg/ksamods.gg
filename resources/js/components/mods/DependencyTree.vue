<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import { show } from '@/routes/mods';

export type TreeNode = {
    id: string | null;
    name: string | null;
    kind: 'loader' | 'required' | 'any_of';
    min?: string | null;
    max?: string | null;
    listed: boolean;
    // Shown higher up in the tree already, so not expanded again.
    repeat: boolean;
    children: TreeNode[];
    any_of?: { id: string; name: string | null }[];
};

/**
 * Everything a mod pulls in, one level per indent, with a guide line down each level.
 */
defineProps<{ nodes: TreeNode[] }>();

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <ul
        class="flex flex-col gap-1 border-l border-[#19140035] pl-4 dark:border-[#3E3E3A]"
    >
        <li v-for="(node, index) in nodes" :key="node.id ?? `any-of-${index}`">
            <p class="flex flex-wrap items-baseline gap-x-2 text-sm">
                <template v-if="node.kind === 'any_of'">
                    <span :class="muted">{{ $t('One of') }}</span>
                    <template v-for="(alt, i) in node.any_of" :key="alt.id">
                        <span v-if="i > 0" :class="muted">{{ $t('or') }}</span>
                        <Link
                            v-if="alt.name"
                            :href="show(alt.id)"
                            class="font-medium hover:underline"
                            >{{ alt.name }}</Link
                        >
                        <span v-else>{{ alt.id }}</span>
                    </template>
                </template>
                <template v-else>
                    <Link
                        v-if="node.name && node.id"
                        :href="show(node.id)"
                        class="font-medium hover:underline"
                        >{{ node.name }}</Link
                    >
                    <span v-else class="font-medium">{{ node.id }}</span>
                    <span
                        v-if="node.kind === 'loader'"
                        class="rounded-full bg-accent-wash px-1.5 text-xs text-accent"
                        >{{ $t('loader') }}</span
                    >
                    <span v-if="node.min" :class="['text-xs', muted]"
                        >{{ node.min }}+</span
                    >
                    <span v-if="!node.listed" :class="['text-xs', muted]">{{
                        $t('not in the index')
                    }}</span>
                    <span v-if="node.repeat" :class="['text-xs', muted]">{{
                        $t('(see above)')
                    }}</span>
                </template>
            </p>
            <DependencyTree
                v-if="node.children.length"
                :nodes="node.children"
                class="mt-1 mb-1"
            />
        </li>
    </ul>
</template>
