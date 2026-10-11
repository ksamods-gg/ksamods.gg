<script setup lang="ts">
import { ref } from 'vue';
import EditableComment from '@/components/mods/EditableComment.vue';
import ReplyForm from '@/components/mods/ReplyForm.vue';
import type { CommentNode } from '@/types';

/**
 * A comment with its replies nested below it, each of which is a CommentNode too.
 */
const props = defineProps<{
    comment: CommentNode;
    modId: string;
    signedIn: boolean;
    // How deep this comment sits; 0 is a top-level comment.
    depth?: number;
}>();

// Past this depth replies stop indenting further, so long threads still fit a phone.
const MAX_INDENT = 4;
const depth = props.depth ?? 0;

const replying = ref(false);
</script>

<template>
    <div>
        <EditableComment
            :comment="comment"
            :signed-in="signedIn"
            :compact="depth > 0"
            @reply="replying = true"
        />

        <div
            v-if="replying || comment.replies.length"
            :class="[
                'mt-3 flex flex-col gap-3',
                depth < MAX_INDENT &&
                    'ml-3 border-l border-[#19140014] pl-3 sm:ml-4 sm:pl-4 dark:border-[#3E3E3A]/60',
            ]"
        >
            <ReplyForm
                v-if="replying"
                :mod-id="modId"
                :target="{ parent_id: comment.id }"
                @close="replying = false"
            />
            <CommentNode
                v-for="reply in comment.replies"
                :key="reply.id"
                :comment="reply"
                :mod-id="modId"
                :signed-in="signedIn"
                :depth="depth + 1"
            />
        </div>
    </div>
</template>
