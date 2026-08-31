import React from 'react';

const DateTime = ({ date, fallback = 'unknown time' }) => {
    if (!date) return <span>{fallback}</span>;

    const d = date instanceof Date ? date : new Date(date);
    if (Number.isNaN(d.getTime())) return <span>{fallback}</span>;

    const userLocale = navigator.language || 'en-US';

    // Formatting options
    const options = {
        year: 'numeric', month: 'long', day: 'numeric',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
        hour12: true,
    };

    const formattedDate = new Intl.DateTimeFormat(userLocale, options).format(d);

    return <span>{formattedDate}</span>;
}

export default DateTime;
