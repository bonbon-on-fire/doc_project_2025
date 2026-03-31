/**
 * Formats a timestamp into a 12-hour AM/PM time string in the user's local timezone.
 * @param timestamp The date to format, can be a Date object or a string.
 * @returns A formatted time string, e.g., "5:09 PM".
 */
export function formatTime(timestamp: Date | string): string {
	// The timestamp can arrive as a string, so we must ensure it's a Date object.
	const date = new Date(timestamp);

	// getHours() and getMinutes() automatically return the time in the browser's local timezone.
	let hours = date.getHours();
	const minutes = date.getMinutes();
	const ampm = hours >= 12 ? 'PM' : 'AM';

	hours = hours % 12;
	hours = hours ? hours : 12; // The hour '0' should be '12'.

	const minutesStr = minutes < 10 ? '0' + minutes : String(minutes);

	return `${hours}:${minutesStr} ${ampm}`;
}
