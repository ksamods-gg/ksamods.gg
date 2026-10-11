import type { NotificationData } from '@/types';

// What happened, in one line, the same in the header's toast and on the notifications page.
export function notificationText(data: NotificationData): string {
    switch (data.kind) {
        case 'reply':
            return `${data.actor.name} replied to your comment on ${data.mod.name}`;
        case 'review_reply':
            return `${data.actor.name} replied to your review of ${data.mod.name}`;
        case 'mention':
            return `${data.actor.name} mentioned you on ${data.mod.name}`;
    }
}

// Each user's private channel, which Laravel's broadcast notifications use.
export const notificationChannel = (userId: number) =>
    `App.Models.User.${userId}`;
