import React from 'react';
import { all, createStarryNight } from '@wooorm/starry-night';
import { toDom } from 'hast-util-to-dom';
import * as log from '../logging';

const starryNight = await createStarryNight(all);
const domParser = new DOMParser();

var selectedCodeBlock;

const CodeHighlighter = ({ code }) => {
    let html = code;

    const handleClick = (target) => {
        const pre = target.closest('pre');
        const btn = target.closest('button');
        if (pre) {
            // if (selectedCodeBlock)
            //     selectedCodeBlock.classList.remove('selected-code-block');
            // selectedCodeBlock = pre;
            // pre.classList.add('selected-code-block');
        }
        if (btn) {
            if (btn.classList.contains('code-block-copy-button')) {
                var clipboardData = btn.getAttribute('code');
                if (clipboardData) {
                    navigator.clipboard.writeText(clipboardData);
                    btn.textContent = 'Done';
                    setTimeout(() => {
                        btn.textContent = 'Copy';
                    }, 3000);
                }
            }
        }
    }

    try {
        const prefix = 'language-';
        const dom = domParser.parseFromString(code, "text/html");
        const nodes = Array.from(dom.body.querySelectorAll('code'));

        for (const node of nodes) {
            const className = Array
                .from(node.classList)
                .find((d) => d.startsWith(prefix));

            if (!className)
                continue;

            const scope = starryNight.flagToScope(className.slice(prefix.length));

            if (!scope)
                continue;

            if (node.textContent === '')
                continue;

            const tree = starryNight.highlight(node.textContent, scope);
            node.replaceChildren(toDom(tree, { fragment: true }));
        };

        const preElements = Array.from(dom.body.querySelectorAll('pre'));

        preElements.forEach((pre) => {
            if (pre.textContent === '')
                return;
            const containerDiv = document.createElement('div');
            containerDiv.classList.add('code-block-div');

            const copyButton = document.createElement('button');
            copyButton.classList.add('code-block-copy-button');
            copyButton.textContent = 'Copy';
            copyButton.setAttribute('code', pre.textContent)

            containerDiv.appendChild(pre.cloneNode(true));
            containerDiv.appendChild(copyButton);

            pre.parentNode.replaceChild(containerDiv, pre)
        });

        html = dom.body.innerHTML;
    }
    catch (ex) {
        log.logError(ex);
    }
    finally {
        return (<div dangerouslySetInnerHTML={{ __html: html }}
            onClick={(e) => {
                const target = e.target
                if (target)
                    handleClick(target);
            }} />);
    }
};

export default CodeHighlighter;