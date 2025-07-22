import React, { useState } from 'react';
import './CodeBlock.css';

interface CodeBlockProps {
  code: string;
  language: string;
  fileName?: string;
  showLineNumbers?: boolean;
}

const CodeBlock: React.FC<CodeBlockProps> = ({
  code,
  language,
  fileName,
  showLineNumbers = true,
}) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (error) {
      console.error('Failed to copy code:', error);
    }
  };

  const getLanguageIcon = (lang: string) => {
    const iconMap: { [key: string]: string } = {
      javascript: 'bi-filetype-js',
      typescript: 'bi-filetype-tsx',
      python: 'bi-filetype-py',
      csharp: 'bi-filetype-cs',
      java: 'bi-filetype-java',
      html: 'bi-filetype-html',
      css: 'bi-filetype-css',
      json: 'bi-filetype-json',
      xml: 'bi-filetype-xml',
      sql: 'bi-database',
      shell: 'bi-terminal',
      bash: 'bi-terminal',
      powershell: 'bi-terminal',
    };
    return iconMap[lang.toLowerCase()] || 'bi-code-slash';
  };

  const formatLanguageName = (lang: string) => {
    const nameMap: { [key: string]: string } = {
      javascript: 'JavaScript',
      typescript: 'TypeScript',
      python: 'Python',
      csharp: 'C#',
      java: 'Java',
      html: 'HTML',
      css: 'CSS',
      json: 'JSON',
      xml: 'XML',
      sql: 'SQL',
      shell: 'Shell',
      bash: 'Bash',
      powershell: 'PowerShell',
    };
    return nameMap[lang.toLowerCase()] || lang.toUpperCase();
  };

  const renderCodeWithLineNumbers = (codeText: string) => {
    const lines = codeText.split('\n');
    return (
      <div className="code-content">
        {showLineNumbers && (
          <div className="line-numbers">
            {lines.map((_, index) => (
              <div key={index} className="line-number">
                {index + 1}
              </div>
            ))}
          </div>
        )}
        <div className="code-text">
          <pre>
            <code>{codeText}</code>
          </pre>
        </div>
      </div>
    );
  };

  return (
    <div className="code-block">
      <div className="code-header">
        <div className="code-info">
          <i className={`bi ${getLanguageIcon(language)}`}></i>
          <span className="language-name">{formatLanguageName(language)}</span>
          {fileName && (
            <>
              <span className="separator">•</span>
              <span className="file-name">{fileName}</span>
            </>
          )}
        </div>
        
        <div className="code-actions">
          <button
            className={`copy-button ${copied ? 'copied' : ''}`}
            onClick={handleCopy}
            title={copied ? 'Copied!' : 'Copy code'}
          >
            <i className={`bi ${copied ? 'bi-check' : 'bi-clipboard'}`}></i>
            <span className="copy-text">{copied ? 'Copied!' : 'Copy'}</span>
          </button>
        </div>
      </div>
      
      <div className="code-container">
        {renderCodeWithLineNumbers(code)}
      </div>
    </div>
  );
};

export default CodeBlock;