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
        case 'release':
            return `${data.subject.name} ${data.version} is out`;
        case 'share_merged':
            return `${data.subject.name} is in the index`;
        case 'share_closed':
            return `The listing pull request of ${data.subject.name} was closed without merging`;
        case 'share_mirrored':
            return `${data.subject.name} ${data.version} has its archive.org copy`;
        case 'share_taken_down':
            return `${data.subject.name} ${data.version} was taken down`;
    }
}

// Who or what the notification shows a picture of.
export function notificationPicture(data: NotificationData): {
    name: string;
    src: string | null;
} {
    return 'actor' in data
        ? { name: data.actor.name, src: data.actor.avatar }
        : { name: data.subject.name, src: data.subject.icon };
}

// The comment excerpt, for notifications about a comment.
export const notificationExcerpt = (data: NotificationData): string | null =>
    'excerpt' in data ? data.excerpt : null;

// Each user's private channel, which Laravel's broadcast notifications use.
export const notificationChannel = (userId: number) =>
    `App.Models.User.${userId}`;
