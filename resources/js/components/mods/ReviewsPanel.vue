<script setup lang="ts">
import { Link, useForm } from '@inertiajs/vue3';
import { ref } from 'vue';
import CommentNode from '@/components/mods/CommentNode.vue';
import ReplyForm from '@/components/mods/ReplyForm.vue';
import SocialPost from '@/components/SocialPost.vue';
import Stars from '@/components/Stars.vue';
import { login } from '@/routes';
import { destroy, save } from '@/routes/reviews';
import type { Reviews } from '@/types';

const props = defineProps<{
    modId: string;
    reviews: Reviews;
    signedIn: boolean;
}>();

const form = useForm({
    rating: props.reviews.mine?.rating ?? 0,
    body: props.reviews.mine?.body ?? '',
});
// The star the pointer is over, previewing the rating before it is picked.
const hovered = ref(0);
// The review whose reply form is open.
const replyingTo = ref<number | null>(null);

function submit() {
    form.put(save(props.modId).url, { preserveScroll: true });
}

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
const input =
    'w-full rounded-md border border-[#19140035] bg-white px-3 py-2 text-sm outline-none focus:border-[#1b1b18] dark:border-[#3E3E3A] dark:bg-[#0a0a0a] dark:focus:border-[#EDEDEC]';
const starLabels = ['Bad', 'Poor', 'Okay', 'Good', 'Great'];
</script>

<template>
    <div class="flex flex-col gap-6">
        <form
            v-if="signedIn"
            class="flex flex-col gap-3"
            @submit.prevent="submit"
        >
            <fieldset>
                <legend class="mb-1 text-sm font-medium">
                    {{ reviews.mine ? 'Your review' : 'Rate this mod' }}
                </legend>
                <div class="flex" @mouseleave="hovered = 0">
                    <label
                        v-for="star in 5"
                        :key="star"
                        class="cursor-pointer p-0.5"
                        @mouseenter="hovered = star"
                    >
                        <input
                            v-model="form.rating"
                            type="radio"
                            name="rating"
                            :value="star"
                            class="peer sr-only"
                        />
                        <svg
                            viewBox="0 0 20 20"
                            aria-hidden="true"
                            :class="[
                                'size-7 rounded-sm peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-[#1b1b18] dark:peer-focus-visible:outline-[#EDEDEC]',
                                star <= (hovered || form.rating)
                                    ? 'fill-[#f5a623]'
                                    : 'fill-[#19140026] dark:fill-[#3E3E3A]',
                            ]"
                        >
                            <path
                                d="M10 1.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L10 14.8l-5.2 2.8 1-5.8L1.5 7.7l5.9-.9L10 1.5z"
                            />
                        </svg>
                        <span class="sr-only"
                            >{{ star }} out of 5,
                            {{ starLabels[star - 1] }}</span
                        >
                    </label>
                </div>
                <p
                    v-if="form.errors.rating"
                    class="mt-1 text-xs text-[#f53003] dark:text-[#FF4433]"
                >
                    {{ form.errors.rating }}
                </p>
            </fieldset>

            <label class="flex flex-col gap-1">
                <span class="sr-only">Your review</span>
                <textarea
                    v-model="form.body"
                    rows="3"
                    maxlength="2000"
                    placeholder="What did you think? (optional)"
                    :class="input"
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
                    :disabled="form.processing || form.rating === 0"
                    class="rounded-md bg-[#1b1b18] px-4 py-2 text-sm font-medium text-white hover:bg-black disabled:opacity-50 dark:bg-[#EDEDEC] dark:text-[#0a0a0a] dark:hover:bg-white"
                >
                    {{ reviews.mine ? 'Update review' : 'Post review' }}
                </button>
            </div>
        </form>
        <p v-else :class="['text-sm', muted]">
            <Link :href="login()" class="underline underline-offset-4"
                >Log in</Link
            >
            to rate this mod.
        </p>

        <div
            v-if="reviews.items.length"
            class="divide-y divide-[#19140014] border-t border-[#19140014] pt-4 dark:divide-[#3E3E3A]/60 dark:border-[#3E3E3A]/60"
        >
            <div
                v-for="review in reviews.items"
                :key="review.id"
                class="py-4 first:pt-0 last:pb-0"
            >
                <SocialPost
                    :post="review"
                    :delete-url="destroy(review.id).url"
                    noun="review"
                    :anchor="`review-${review.id}`"
                >
                    <template v-if="signedIn" #actions>
                        <button
                            type="button"
                            :class="['hover:underline', muted]"
                            @click="replyingTo = review.id"
                        >
                            Reply
                        </button>
                    </template>
                    <Stars :rating="review.rating" class="mt-1" />
                    <p
                        v-if="review.body"
                        class="mt-1 text-sm break-words whitespace-pre-line"
                    >
                        {{ review.body }}
                    </p>
                </SocialPost>

                <!-- Comments on the review nest the same way as the discussion's. -->
                <div
                    v-if="replyingTo === review.id || review.replies.length"
                    class="mt-3 ml-3 flex flex-col gap-3 border-l border-[#19140014] pl-3 sm:ml-4 sm:pl-4 dark:border-[#3E3E3A]/60"
                >
                    <ReplyForm
                        v-if="replyingTo === review.id"
                        :mod-id="modId"
                        :target="{ review_id: review.id }"
                        @close="replyingTo = null"
                    />
                    <CommentNode
                        v-for="reply in review.replies"
                        :key="reply.id"
                        :comment="reply"
                        :mod-id="modId"
                        :signed-in="signedIn"
                        :depth="1"
                    />
                </div>
            </div>
        </div>
        <p v-else :class="['text-sm', muted]">No reviews yet.</p>
    </div>
</template>
