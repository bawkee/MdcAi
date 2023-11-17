import React from 'react';

const DateTime = ({ date }) => {
    const userLocale = navigator.language || 'en-US';

    // Formatting options
    const options = {
        year: 'numeric', month: 'long', day: 'numeric',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
        hour12: true,
    };

    const formattedDate = new Intl.DateTimeFormat(userLocale, options).format(date);

    return <span>{formattedDate}</span>;
}

export default DateTime;